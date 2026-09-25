using LMS.Application.Interfaces.QuizAttempts;

namespace LMS.Application.Features.QuizAttempts.GetQuizAttempts
{
    public sealed class GetQuizAttemptsHandler
    {
        private readonly IQuizAttemptRepository _attempts;

        public GetQuizAttemptsHandler(IQuizAttemptRepository attempts) => _attempts = attempts;

        public async Task<IReadOnlyList<GetQuizAttemptsResponse>> HandleAsync(Guid quizId,
            Guid userId, CancellationToken cancellationToken = default)
        {
            var attempts = await _attempts.GetByQuizAndUserAsync(quizId, userId, cancellationToken);

            return attempts
                .OrderByDescending(x => x.StartedAt)
                .ThenByDescending(x => x.AttemptNumber)
                .Select(x => new GetQuizAttemptsResponse
                {
                    AttemptId = x.Id,
                    AttemptNumber = x.AttemptNumber,
                    Status = x.Status,
                    // SQL Server preserves the UTC clock value but not DateTime.Kind.
                    StartedAt = DateTime.SpecifyKind(x.StartedAt, DateTimeKind.Utc),
                    Deadline = DateTime.SpecifyKind(x.Deadline, DateTimeKind.Utc),
                    SubmittedAt = x.SubmittedAt is { } submittedAt
                        ? DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc)
                        : null,
                    EarnedMarks = x.EarnedMarks,
                    PossibleMarks = x.PossibleMarks,
                    IsPassed = x.IsPassed
                })
                .ToList();
        }
    }
}
