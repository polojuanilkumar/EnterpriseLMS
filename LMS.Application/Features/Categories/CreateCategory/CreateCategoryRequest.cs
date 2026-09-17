using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.CreateCategory
{
    public sealed class CreateCategoryRequest
    {
        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
    }
}
