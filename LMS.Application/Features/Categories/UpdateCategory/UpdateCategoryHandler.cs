using LMS.Application.Features.Categories.Common;
using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.UpdateCategory
{
    public sealed class UpdateCategoryHandler
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateCategoryHandler(
            ICategoryRepository categoryRepository,
            IUnitOfWork unitOfWork)
        {
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<CategoryResponse> HandleAsync(
            Guid categoryId,
            UpdateCategoryRequest request,
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

            var name = request.Name.Trim();
            var description = request.Description.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Category name is required.");
            }

            var exists =
                await _categoryRepository.ExistsByNameAsync(
                    name,
                    cancellationToken);

            if (exists && !string.Equals(
                category.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A category with the same name already exists.");
            }

            category.Update(
                name,
                description);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

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
