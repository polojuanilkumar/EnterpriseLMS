using LMS.Application.Features.Quizzes.Common;
using LMS.Application.Interfaces.QuizAttempts;
using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.QuizAttempts.SubmitQuizAttempt
{
    public sealed class SubmitQuizAttemptHandler
    {
        private readonly IQuizAttemptRepository _attempts;
        private readonly IQuizRepository _quizzes;
        private readonly IQuizQuestionRepository _questions;
        private readonly IQuizOptionRepository _options;
        private readonly TimeProvider _timeProvider;
        private readonly EnsureQuizReadable _readable;

        public SubmitQuizAttemptHandler(IQuizAttemptRepository attempts, IQuizRepository quizzes,
            IQuizQuestionRepository questions, IQuizOptionRepository options, TimeProvider timeProvider,
            EnsureQuizReadable readable)
        {
            _attempts = attempts;
            _quizzes = quizzes;
            _questions = questions;
            _options = options;
            _timeProvider = timeProvider;
            _readable = readable;
        }

        public async Task<SubmitQuizAttemptResponse> HandleAsync(Guid quizId, Guid attemptId,
            Guid userId, SubmitQuizAttemptRequest request, CancellationToken cancellationToken = default)
        {
            var outcome = await _attempts.ExecuteTransactionAsync(quizId, async token =>
            {
                // Query is scoped to both the route quiz and the authenticated owner.
                var attempts = await _attempts.GetByQuizAndUserAsync(quizId, userId, token);
                var attempt = attempts.SingleOrDefault(x => x.Id == attemptId)
                    ?? throw new KeyNotFoundException("Quiz attempt not found.");
                if (attempt.Status != QuizAttemptStatus.Started)
                    throw new InvalidOperationException("Only a started attempt can be submitted.");

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (attempt.Expire(now))
                {
                    await _attempts.SaveChangesAsync(token);
                    return new SubmitOutcome(null, "The attempt deadline has passed.");
                }

                var quiz = await _quizzes.GetByIdAsync(quizId, token);
                if (quiz is null || !quiz.IsPublished)
                    throw new KeyNotFoundException("Published quiz not found.");

                // Recheck current access inside the submission transaction before scoring or saving answers.
                await _readable.CheckAsync(quiz, userId, canViewUnpublished: false, token);

                if (request is null || request.Answers is null || request.Answers.Any(x => x is null))
                    throw new ArgumentException("Answers must be a non-null list.");
                if (request.Answers.Select(x => x.QuestionId).Distinct().Count() != request.Answers.Count)
                    throw new ArgumentException("Duplicate question IDs are not allowed.");

                var questions = await _questions.GetByQuizIdAsync(quizId, token);
                var questionIds = questions.Select(x => x.Id).ToHashSet();
                if (request.Answers.Any(x => !questionIds.Contains(x.QuestionId)))
                    throw new ArgumentException("An answer references a question outside this quiz.");
                var possibleMarks = questions.Sum(x => x.Marks);
                if (QuizConfigurationValidation.GetQuestionsError(questions) is not null
                    || possibleMarks != attempt.PossibleMarks)
                    throw new InvalidOperationException("Quiz marks changed or are invalid for this attempt.");

                var submittedAnswers = request.Answers.ToDictionary(x => x.QuestionId);
                var answers = new List<QuizAttemptAnswer>();
                var selections = new List<QuizAttemptAnswerOption>();
                decimal earnedMarks = 0;
                foreach (var question in questions)
                {
                    var selectedIds = submittedAnswers.TryGetValue(question.Id, out var submitted)
                        ? submitted.SelectedOptionIds : new List<Guid>();
                    if (selectedIds is null || selectedIds.Distinct().Count() != selectedIds.Count)
                        throw new ArgumentException("Selected option IDs must be non-null and unique per question.");
                    if ((question.QuestionType is QuestionType.SingleChoice or QuestionType.TrueFalse)
                        && selectedIds.Count > 1)
                        throw new ArgumentException("SingleChoice and TrueFalse answers require one selected option when answered.");

                    var options = await _options.GetByQuestionIdAsync(question.Id, token);
                    var optionById = options.ToDictionary(x => x.Id);
                    if (selectedIds.Any(id => !optionById.ContainsKey(id)))
                        throw new ArgumentException("A selected option does not belong to its question.");
                    var correctIds = options.Where(x => x.IsCorrect).Select(x => x.Id).ToHashSet();
                    var error = QuizConfigurationValidation.GetQuestionError(question, options);
                    if (error is not null)
                        throw new InvalidOperationException(error);

                    // Empty selections always score zero. All question types use exact-set matching.
                    var isCorrect = selectedIds.Count > 0 && correctIds.SetEquals(selectedIds);
                    var awardedMarks = isCorrect ? question.Marks : 0m;
                    earnedMarks += awardedMarks;
                    var answer = new QuizAttemptAnswer(attempt.Id, question.Id, question.QuestionText,
                        question.QuestionType, question.Marks, awardedMarks, isCorrect);
                    answers.Add(answer);
                    foreach (var optionId in selectedIds)
                        selections.Add(new QuizAttemptAnswerOption(answer.Id, optionId, optionById[optionId].OptionText));
                }

                var percentage = earnedMarks / possibleMarks * 100m;
                // Compare without display rounding against the threshold saved at start.
                var isPassed = earnedMarks * 100m >= attempt.PassingPercentageSnapshot * possibleMarks;
                attempt.Submit(now, earnedMarks, isPassed);
                await _attempts.AddAnswersAsync(answers, selections, token);
                await _attempts.SaveChangesAsync(token);
                return new SubmitOutcome(new SubmitQuizAttemptResponse
                {
                    AttemptId = attempt.Id,
                    EarnedMarks = earnedMarks,
                    PossibleMarks = possibleMarks,
                    Percentage = decimal.Round(percentage, 2, MidpointRounding.AwayFromZero),
                    IsPassed = isPassed,
                    RemainingAttempts = Math.Max(0, quiz.MaxAttempts - attempts.Count)
                }, null);
            }, cancellationToken);

            // Commit expiration before reporting a late submission as a conflict.
            if (outcome.Error is not null)
                throw new InvalidOperationException(outcome.Error);
            return outcome.Response!;
        }

        private sealed record SubmitOutcome(SubmitQuizAttemptResponse? Response, string? Error);
    }
}
