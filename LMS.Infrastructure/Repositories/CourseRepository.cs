using LMS.Application.Interfaces.Courses;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class CourseRepository : ICourseRepository
    {
        private readonly LMSDbContext _context;

        public CourseRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            Course course,
            CancellationToken cancellationToken = default)
        {
            course.AssignOrganization(_context.CurrentOrganizationId);
            await _context.Courses.AddAsync(
                course,
                cancellationToken);
        }

        public async Task<Course?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.Courses
                .Where(x => _context.CanManageContent || x.IsPublished)
                .Where(x => !_context.RestrictToAssignedCourses || x.InstructorId == _context.CurrentUserId)
                 .Include(x => x.Category)
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<bool> ExistsByCodeAsync(
            string code,
            CancellationToken cancellationToken = default)
        {
            return await _context.Courses
                .AnyAsync(
                    x => x.Code == code,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<Course>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.Courses
                .AsNoTracking()
                .Where(x => _context.CanManageContent || x.IsPublished)
                .Where(x => !_context.RestrictToAssignedCourses || x.InstructorId == _context.CurrentUserId)
                .Include(x => x.Category)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
