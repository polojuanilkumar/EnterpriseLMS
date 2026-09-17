using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.UpdateCourse
{
    public sealed class UpdateCourseRequest
    {
        public string Title { get; init; } = string.Empty;

        public string Code { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public Guid? CategoryId { get; init; }
        public CourseLevel Level { get; init; }

        public int DurationInMinutes { get; init; }

        public string? ThumbnailUrl { get; init; }
    }
}
