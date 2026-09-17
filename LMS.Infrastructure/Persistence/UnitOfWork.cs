using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Persistence
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private readonly LMSDbContext _context;

        public UnitOfWork(LMSDbContext context)
        {
            _context = context;
        }

        public async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(
                cancellationToken);
        }
    }
}
