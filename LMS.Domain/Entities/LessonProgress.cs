using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class LessonProgress
    {
        private LessonProgress()
        {
        }

        public LessonProgress(
            Guid userId,
            Guid lessonId)
        {
            Id = Guid.NewGuid();
            UserId = userId;
            LessonId = lessonId;
            IsCompleted = false;
            StartedAt = DateTime.UtcNow;
            ProgressPercentage = 0;
            LastAccessedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public Guid LessonId { get; private set; }

        public bool IsCompleted { get; private set; }

        public DateTime? CompletedAt { get; private set; }

        public DateTime StartedAt { get; private set; }

        public decimal ProgressPercentage { get; private set; }

        public DateTime LastAccessedAt { get; private set; }

        public void UpdateProgress(decimal progressPercentage)
        {
            if (progressPercentage < 0 ||
                progressPercentage > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(progressPercentage),
                    "Progress percentage must be between 0 and 100.");
            }

            ProgressPercentage = progressPercentage;
            LastAccessedAt = DateTime.UtcNow;

            if (progressPercentage == 100)
            {
                Complete();
            }
        }

        public void Complete()
        {
            IsCompleted = true;
            ProgressPercentage = 100;
            CompletedAt = DateTime.UtcNow;
            LastAccessedAt = DateTime.UtcNow;
        }

        public void Access()
        {
            LastAccessedAt = DateTime.UtcNow;
        }
    }
}
