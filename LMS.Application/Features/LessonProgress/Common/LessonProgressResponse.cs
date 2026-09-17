using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.LessonProgress.Common
{
    public sealed class LessonProgressResponse
    {
        public Guid Id { get; init; }

        public Guid UserId { get; init; }

        public Guid LessonId { get; init; }

        public bool IsCompleted { get; init; }

        public DateTime? CompletedAt { get; init; }

        public DateTime StartedAt { get; init; }

        public decimal ProgressPercentage { get; init; }

        public DateTime LastAccessedAt { get; init; }
    }
}
