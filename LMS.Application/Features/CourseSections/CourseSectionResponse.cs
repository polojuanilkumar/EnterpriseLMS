using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections
{
    public sealed class CourseSectionResponse
    {
        public Guid Id { get; init; }

        public Guid CourseId { get; init; }

        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public int DisplayOrder { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
