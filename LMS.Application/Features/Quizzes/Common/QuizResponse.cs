using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class QuizResponse
    {
        public Guid Id { get; init; }

        public Guid LessonId { get; init; }

        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public decimal PassingPercentage { get; init; }

        public int TimeLimitInMinutes { get; init; }

        public int MaxAttempts { get; init; }

        public bool IsPublished { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
