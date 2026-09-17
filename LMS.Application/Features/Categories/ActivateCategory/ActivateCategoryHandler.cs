using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Categories.ActivateCategory
{
    public sealed class ActivateCategoryHandler
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ActivateCategoryHandler(
            ICategoryRepository categoryRepository,
            IUnitOfWork unitOfWork)
        {
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
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

            category.Activate();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
