using LMS.Application.Interfaces.Enrollments;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
