using LMS.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.Domain.Entities
{
    public sealed class QuizAttempt
    {
        private QuizAttempt() { }

        public QuizAttempt(Guid quizId, Guid userId, int attemptNumber,
            DateTime startedAt, DateTime deadline, decimal possibleMarks,
            decimal passingPercentageSnapshot)
        {
            Id = Guid.NewGuid();
            QuizId = quizId;
            UserId = userId;
            AttemptNumber = attemptNumber;
            StartedAt = startedAt;
            Deadline = deadline;
            PossibleMarks = possibleMarks;
            PassingPercentageSnapshot = passingPercentageSnapshot;
            Status = QuizAttemptStatus.Started;
        }

        public Guid Id { get; private set; }
        public Guid QuizId { get; private set; }
        public Guid UserId { get; private set; }
        public int AttemptNumber { get; private set; }
        public DateTime StartedAt { get; private set; }
        public DateTime Deadline { get; private set; }
        public DateTime? SubmittedAt { get; private set; }
        public decimal? EarnedMarks { get; private set; }
        public decimal PossibleMarks { get; private set; }
        public decimal PassingPercentageSnapshot { get; private set; }
        public bool? IsPassed { get; private set; }
        public QuizAttemptStatus Status { get; private set; }

        // Creation consumes a slot immediately. MaxAttempts counts all records,
        // regardless of status; neither submission nor expiration releases a slot.
        [NotMapped]
        public bool ConsumesAttempt => true;

        /// <summary>
        /// Expires an overdue started attempt. The caller must persist the transition.
        /// Pass the current server UTC time; the deadline itself is still eligible.
        /// Persist expiration before creating another attempt for this student and quiz.
        /// </summary>
        public bool Expire(DateTime utcNow)
        {
            if (Status != QuizAttemptStatus.Started || utcNow <= Deadline)
                return false;

            Status = QuizAttemptStatus.Expired;
            return true;
        }

        public void Submit(DateTime submittedAt, decimal earnedMarks, bool isPassed)
        {
            if (Status != QuizAttemptStatus.Started)
                throw new InvalidOperationException("Only a started attempt can be submitted.");

            // Rejection is side-effect free. The caller explicitly expires and saves
            // an overdue attempt before returning the submission error.
            if (submittedAt > Deadline)
                throw new InvalidOperationException("The attempt deadline has passed.");

            if (earnedMarks < 0 || earnedMarks > PossibleMarks)
                throw new ArgumentOutOfRangeException(nameof(earnedMarks));

            SubmittedAt = submittedAt;
            EarnedMarks = earnedMarks;
            IsPassed = isPassed;
            Status = QuizAttemptStatus.Submitted;
        }
    }
}
