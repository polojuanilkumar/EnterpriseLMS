using LMS.Application.Features.Categories.Common;
using LMS.Application.Interfaces.Categories;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.GetCategoryById
{
    public sealed class GetCategoryByIdHandler
    {
        private readonly ICategoryRepository _categoryRepository;

        public GetCategoryByIdHandler(
            ICategoryRepository categoryRepository)
        {
            _categoryRepository = categoryRepository;
        }

        public async Task<CategoryResponse> HandleAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default)
        {
            var category =
                await _categoryRepository.GetByIdAsync(
                    categoryId,
                    cancellationToken);

            if (category is null)
            {
                throw new KeyNotFoundException(
                    "Category not found.");
            }

            return new CategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                IsActive = category.IsActive,
                CreatedAt = category.CreatedAt,
                UpdatedAt = category.UpdatedAt
            };
        }
    }
}
