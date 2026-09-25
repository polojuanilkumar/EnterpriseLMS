using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Lessons;
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
        private readonly ILessonRepository _lessons;
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseEnrollmentRepository _enrollments;
        private readonly IQuizQuestionRepository _questions;
        private readonly TimeProvider _timeProvider;

        public StartQuizAttemptHandler(IQuizAttemptRepository attempts, IQuizRepository quizzes,
            ILessonRepository lessons, ICourseSectionRepository sections,
            ICourseEnrollmentRepository enrollments, IQuizQuestionRepository questions,
            TimeProvider timeProvider)
        {
            _attempts = attempts;
            _quizzes = quizzes;
            _lessons = lessons;
            _sections = sections;
            _enrollments = enrollments;
            _questions = questions;
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

                var lesson = await _lessons.GetByIdAsync(quiz.LessonId, token)
                    ?? throw new KeyNotFoundException("Lesson not found.");
                var section = await _sections.GetByIdAsync(lesson.SectionId, token)
                    ?? throw new KeyNotFoundException("Course section not found.");
                var enrollment = await _enrollments.GetByCourseAndUserAsync(section.CourseId, userId, token);
                if (enrollment is null || enrollment.Status != EnrollmentStatus.Active)
                    throw new UnauthorizedAccessException("An active course enrollment is required.");

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
                if (questions.Count == 0 || questions.Any(x => x.Marks <= 0) || quiz.TimeLimitInMinutes <= 0)
                    return new StartOutcome(null, "The quiz is not configured for attempts.");

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
