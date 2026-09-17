using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Entities
{
    public sealed class Quiz
    {
        private Quiz()
        {
        }

        public Quiz(
            Guid lessonId,
            string title,
            string? description,
            decimal passingPercentage,
            int timeLimitInMinutes,
            int maxAttempts)
        {
            Id = Guid.NewGuid();
            LessonId = lessonId;
            Title = title;
            Description = description;
            PassingPercentage = passingPercentage;
            TimeLimitInMinutes = timeLimitInMinutes;
            MaxAttempts = maxAttempts;
            IsPublished = false;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }

        public Guid LessonId { get; private set; }

        public string Title { get; private set; } = null!;

        public string? Description { get; private set; }

        public decimal PassingPercentage { get; private set; }

        public int TimeLimitInMinutes { get; private set; }

        public int MaxAttempts { get; private set; }

        public bool IsPublished { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

        public void Update(
            string title,
            string? description,
            decimal passingPercentage,
            int timeLimitInMinutes,
            int maxAttempts)
        {
            Title = title;
            Description = description;
            PassingPercentage = passingPercentage;
            TimeLimitInMinutes = timeLimitInMinutes;
            MaxAttempts = maxAttempts;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Publish()
        {
            IsPublished = true;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Unpublish()
        {
            IsPublished = false;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
