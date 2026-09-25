using LMS.Application.Interfaces.QuizAttempts;
using LMS.Domain.Enums;

namespace LMS.Application.Features.QuizAttempts.GetQuizAttemptResult
{
    public sealed class GetQuizAttemptResultHandler
    {
        private readonly IQuizAttemptRepository _attempts;

        public GetQuizAttemptResultHandler(IQuizAttemptRepository attempts) => _attempts = attempts;

        public async Task<GetQuizAttemptResultResponse> HandleAsync(Guid quizId, Guid attemptId,
            Guid userId, CancellationToken cancellationToken = default)
        {
            var attempt = await _attempts.GetByIdAndQuizAndUserAsync(
                attemptId, quizId, userId, cancellationToken)
                ?? throw new KeyNotFoundException("Quiz attempt not found.");

            if (attempt.Status != QuizAttemptStatus.Submitted)
                throw new InvalidOperationException("Results are only available for submitted quiz attempts.");

            return new GetQuizAttemptResultResponse
            {
                AttemptId = attempt.Id,
                EarnedMarks = attempt.EarnedMarks,
                PossibleMarks = attempt.PossibleMarks,
                PassingPercentageSnapshot = attempt.PassingPercentageSnapshot,
                // SQL Server preserves the UTC clock value but not DateTime.Kind.
                SubmittedAt = attempt.SubmittedAt is { } submittedAt
                    ? DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc)
                    : null,
                IsPassed = attempt.IsPassed
            };
        }
    }
}
