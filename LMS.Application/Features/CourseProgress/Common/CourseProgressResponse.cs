using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseProgress.Common
{
    public sealed class CourseProgressResponse
    {
        public Guid CourseId { get; init; }

        public Guid UserId { get; init; }

        public int TotalLessons { get; init; }

        public int CompletedLessons { get; init; }

        public decimal ProgressPercentage { get; init; }

        public bool IsCompleted { get; init; }

        /// <summary>Stored enrollment status, independent of current published-lesson progress.</summary>
        public EnrollmentStatus EnrollmentStatus { get; init; }

        /// <summary>Stored enrollment completion timestamp, or null if none is recorded.</summary>
        public DateTime? EnrollmentCompletedAt { get; init; }
    }
}
