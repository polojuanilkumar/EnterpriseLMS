using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections
{
    public sealed class UpdateCourseSectionRequest
    {
        public string Title { get; init; } = null!;

        public string? Description { get; init; }

        public int DisplayOrder { get; init; }
    }
}
