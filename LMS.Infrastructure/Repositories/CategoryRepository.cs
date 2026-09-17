using LMS.Application.Interfaces.Categories;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class CategoryRepository : ICategoryRepository
    {
        private readonly LMSDbContext _context;

        public CategoryRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            Category category,
            CancellationToken cancellationToken = default)
        {
            await _context.Categories.AddAsync(
                category,
                cancellationToken);
        }

        public async Task<Category?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .AnyAsync(
                    x => x.Name == name,
                    cancellationToken);
        }
    }
}
