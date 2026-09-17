using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Lessons.Common
{
    public sealed class LessonResponse
    {
        public Guid Id { get; init; }

        public Guid SectionId { get; init; }

        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public string LessonType { get; init; } = null!;

        public string? Content { get; init; }

        public string? VideoUrl { get; init; }

        public int DurationInMinutes { get; init; }

        public int DisplayOrder { get; init; }

        public bool IsPublished { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
