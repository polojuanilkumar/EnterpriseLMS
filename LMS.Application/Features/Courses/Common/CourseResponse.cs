using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.Common
{
    public sealed class CourseResponse
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Code { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
        public Guid? CategoryId { get; init; }

        public string? CategoryName { get; init; }

        public Guid InstructorId { get; init; }

        public string Level { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public int DurationInMinutes { get; init; }

        public string? ThumbnailUrl { get; init; }

        public bool IsPublished { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
