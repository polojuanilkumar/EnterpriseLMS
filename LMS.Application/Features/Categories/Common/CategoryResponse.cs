using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.Common
{
    public sealed class CategoryResponse
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }
}
