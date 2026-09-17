using LMS.Application.Features.Categories.Common;
using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.CreateCategory
{
    public sealed class CreateCategoryHandler
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateCategoryHandler(
            ICategoryRepository categoryRepository,
            IUnitOfWork unitOfWork)
        {
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<CategoryResponse> HandleAsync(
            CreateCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
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

            if (exists)
            {
                throw new InvalidOperationException(
                    "A category with the same name already exists.");
            }

            var category = new Category(
                name,
                description);

            await _categoryRepository.AddAsync(
                category,
                cancellationToken);

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
