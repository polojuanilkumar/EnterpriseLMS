using LMS.Application.Features.Categories.Common;
using LMS.Application.Interfaces.Categories;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.GetCategories
{
    public sealed class GetCategoriesHandler
    {
        private readonly ICategoryRepository _categoryRepository;

        public GetCategoriesHandler(
            ICategoryRepository categoryRepository)
        {
            _categoryRepository = categoryRepository;
        }

        public async Task<IReadOnlyList<CategoryResponse>> HandleAsync(
            CancellationToken cancellationToken = default)
        {
            var categories =
                await _categoryRepository.GetAllAsync(
                    cancellationToken);

            return categories
                .Select(category => new CategoryResponse
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    IsActive = category.IsActive,
                    CreatedAt = category.CreatedAt,
                    UpdatedAt = category.UpdatedAt
                })
                .ToList();
        }
    }
}
