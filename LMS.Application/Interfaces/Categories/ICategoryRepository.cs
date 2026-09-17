using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Categories
{
    public interface ICategoryRepository
    {
        Task AddAsync(
            Category category,
            CancellationToken cancellationToken = default);

        Task<Category?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Category>> GetAllAsync(
            CancellationToken cancellationToken = default);

        Task<bool> ExistsByNameAsync(
            string name,
            CancellationToken cancellationToken = default);
    }
}
