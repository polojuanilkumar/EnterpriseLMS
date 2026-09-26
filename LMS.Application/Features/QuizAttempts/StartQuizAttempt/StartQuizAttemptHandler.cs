using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizAttempts;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.QuizAttempts.StartQuizAttempt
{
    public sealed class StartQuizAttemptHandler
    {
        private readonly IQuizAttemptRepository _attempts;
        private readonly IQuizRepository _quizzes;
        private readonly EnsureQuizReadable _readable;
        private readonly IQuizQuestionRepository _questions;
        private readonly IQuizOptionRepository _options;
        private readonly TimeProvider _timeProvider;

        public StartQuizAttemptHandler(IQuizAttemptRepository attempts, IQuizRepository quizzes,
            EnsureQuizReadable readable, IQuizQuestionRepository questions,
            IQuizOptionRepository options, TimeProvider timeProvider)
        {
            _attempts = attempts;
            _quizzes = quizzes;
            _readable = readable;
            _questions = questions;
            _options = options;
            _timeProvider = timeProvider;
        }

        public async Task<StartQuizAttemptResponse> HandleAsync(Guid quizId, Guid userId,
            CancellationToken cancellationToken = default)
        {
            var outcome = await _attempts.ExecuteTransactionAsync(quizId, async token =>
            {
                var quiz = await _quizzes.GetByIdAsync(quizId, token);
                if (quiz is null || !quiz.IsPublished)
                    throw new KeyNotFoundException("Published quiz not found.");

                await _readable.CheckAsync(quiz, userId, canViewUnpublished: false, token);

                var attempts = await _attempts.GetByQuizAndUserAsync(quizId, userId, token);
                // Capture server time after acquiring the lock, not while waiting for it.
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var expired = false;
                foreach (var attempt in attempts)
                    expired |= attempt.Expire(now);

                // Flush expirations before insert to release the filtered unique index.
                if (expired)
                    await _attempts.SaveChangesAsync(token);

                if (attempts.Any(x => x.Status == QuizAttemptStatus.Started))
                    return new StartOutcome(null, "A quiz attempt is already active.");
                if (attempts.Count >= quiz.MaxAttempts)
                    return new StartOutcome(null, "Maximum quiz attempts reached.");

                var questions = await _questions.GetByQuizIdAsync(quizId, token);
                if (quiz.TimeLimitInMinutes <= 0)
                    return new StartOutcome(null, "The quiz is not configured for attempts.");
                var error = QuizConfigurationValidation.GetQuestionsError(questions);
                if (error is not null)
                    return new StartOutcome(null, error);
                foreach (var question in questions)
                {
                    var options = await _options.GetByQuestionIdAsync(question.Id, token);
                    error = QuizConfigurationValidation.GetQuestionError(question, options);
                    if (error is not null)
                        return new StartOutcome(null, error);
                }

                var nextNumber = attempts.Count == 0 ? 1 : checked(attempts.Max(x => x.AttemptNumber) + 1);
                var newAttempt = new QuizAttempt(quizId, userId, nextNumber, now,
                    now.AddMinutes(quiz.TimeLimitInMinutes), questions.Sum(x => x.Marks), quiz.PassingPercentage);
                await _attempts.AddAsync(newAttempt, token);
                await _attempts.SaveChangesAsync(token);

                return new StartOutcome(new StartQuizAttemptResponse
                {
                    AttemptId = newAttempt.Id,
                    AttemptNumber = newAttempt.AttemptNumber,
                    StartedAt = DateTime.SpecifyKind(newAttempt.StartedAt, DateTimeKind.Utc),
                    Deadline = DateTime.SpecifyKind(newAttempt.Deadline, DateTimeKind.Utc),
                    RemainingAttempts = quiz.MaxAttempts - attempts.Count - 1
                }, null);
            }, cancellationToken);

            // Throw only after committing any expiration, including when the limit is reached.
            if (outcome.Error is not null)
                throw new InvalidOperationException(outcome.Error);
            return outcome.Response!;
        }

        private sealed record StartOutcome(StartQuizAttemptResponse? Response, string? Error);
    }
}
