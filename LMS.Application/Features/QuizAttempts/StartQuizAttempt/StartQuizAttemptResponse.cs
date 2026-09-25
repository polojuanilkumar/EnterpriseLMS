namespace LMS.Application.Features.QuizAttempts.StartQuizAttempt
{
    public sealed class StartQuizAttemptResponse
    {
        public Guid AttemptId { get; init; }
        public int AttemptNumber { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime Deadline { get; init; }
        public int RemainingAttempts { get; init; }
    }
}
