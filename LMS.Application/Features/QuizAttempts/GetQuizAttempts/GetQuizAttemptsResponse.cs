using LMS.Domain.Enums;

namespace LMS.Application.Features.QuizAttempts.GetQuizAttempts
{
    public sealed class GetQuizAttemptsResponse
    {
        public Guid AttemptId { get; init; }
        public int AttemptNumber { get; init; }
        public QuizAttemptStatus Status { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime Deadline { get; init; }
        public DateTime? SubmittedAt { get; init; }
        public decimal? EarnedMarks { get; init; }
        public decimal PossibleMarks { get; init; }
        public bool? IsPassed { get; init; }
    }
}
