using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.Common
{
    public sealed class CreateLessonRequest
    {
        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public LessonType LessonType { get; init; }

        public string? Content { get; init; }

        public string? VideoUrl { get; init; }

        public int DurationInMinutes { get; init; }

        public int DisplayOrder { get; init; }
    }
}
