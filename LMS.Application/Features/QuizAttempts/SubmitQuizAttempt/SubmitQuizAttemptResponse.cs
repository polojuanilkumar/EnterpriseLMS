namespace LMS.Application.Features.QuizAttempts.SubmitQuizAttempt
{
    public sealed class SubmitQuizAttemptResponse
    {
        public Guid AttemptId { get; init; }
        public decimal EarnedMarks { get; init; }
        public decimal PossibleMarks { get; init; }
        public decimal Percentage { get; init; }
        public bool IsPassed { get; init; }
        public int RemainingAttempts { get; init; }
    }
}
