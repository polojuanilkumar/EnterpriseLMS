using LMS.Application.Interfaces.Enrollments;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class CourseEnrollmentRepository
        : ICourseEnrollmentRepository
    {
        private readonly LMSDbContext _context;

        public CourseEnrollmentRepository(
            LMSDbContext context)
        {
            _context = context;
        }

        public async Task ExecuteCompletionTransactionAsync(Guid enrollmentId,
            Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                // Serialize repeated completion requests before loading tracked enrollment state.
                // Serializable also holds prerequisite reads stable until the completion is saved.
                await _context.CourseEnrollments.FromSqlInterpolated(
                    $"SELECT * FROM [CourseEnrollments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {enrollmentId}")
                    .AsNoTracking().ToListAsync(cancellationToken);

                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                throw new InvalidOperationException(
                    "A conflicting enrollment operation occurred. Refresh and try again.", exception);
            }
        }

        private static bool IsDeadlock(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => error.Number == 1205))
                    return true;
            }
            return false;
        }

        public async Task AddAsync(
            CourseEnrollment enrollment,
            CancellationToken cancellationToken = default)
        {
            await _context.CourseEnrollments.AddAsync(
                enrollment,
                cancellationToken);
        }

        public async Task<CourseEnrollment?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.CourseEnrollments
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<CourseEnrollment?> GetByCourseAndUserAsync(
            Guid courseId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.CourseEnrollments
                .FirstOrDefaultAsync(
                    x => x.CourseId == courseId &&
                         x.UserId == userId,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<CourseEnrollment>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.CourseEnrollments
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.EnrolledAt)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
