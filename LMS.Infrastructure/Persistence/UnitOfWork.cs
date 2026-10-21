using LMS.Application.Interfaces.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
            try
            {
                return await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateEnrollment(exception))
            {
                throw new InvalidOperationException(
                    "Student is already enrolled in this course.", exception);
            }
        }

        private static bool IsDuplicateEnrollment(DbUpdateException exception)
        {
            // Match this index specifically so unrelated database failures retain their handling.
            for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
            {
                if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error =>
                    error.Number is 2601 or 2627 &&
                    error.Message.Contains("'IX_CourseEnrollments_CourseId_UserId'", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
