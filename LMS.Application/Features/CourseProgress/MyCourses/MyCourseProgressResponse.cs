using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseProgress.MyCourses
{
    public sealed class MyCourseProgressResponse
    {
        public Guid CourseId { get; init; }

        public string CourseTitle { get; init; } = null!;

        public string CourseCode { get; init; } = null!;

        public int TotalLessons { get; init; }

        public int CompletedLessons { get; init; }

        public decimal ProgressPercentage { get; init; }

        public bool IsCompleted { get; init; }
    }
}
