namespace LMS.Application.Features.QuizAttempts.GetQuizAttemptResult
{
    public sealed class GetQuizAttemptResultResponse
    {
        public Guid AttemptId { get; init; }
        public decimal? EarnedMarks { get; init; }
        public decimal PossibleMarks { get; init; }
        public decimal PassingPercentageSnapshot { get; init; }
        public DateTime? SubmittedAt { get; init; }
        public bool? IsPassed { get; init; }
    }
}
