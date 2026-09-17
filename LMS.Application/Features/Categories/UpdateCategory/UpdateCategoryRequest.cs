using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.UpdateCategory
{
    public sealed class UpdateCategoryRequest
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
