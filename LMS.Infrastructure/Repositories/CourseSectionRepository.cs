using LMS.Application.Interfaces.CourseSections;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class CourseSectionRepository
          : ICourseSectionRepository
    {
        private readonly LMSDbContext _context;

        public CourseSectionRepository(
            LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            CourseSection section,
            CancellationToken cancellationToken = default)
        {
            await _context.CourseSections.AddAsync(
                section,
                cancellationToken);
        }

        public async Task<CourseSection?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.CourseSections
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<CourseSection>> GetByCourseIdAsync(
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            return await _context.CourseSections
                .AsNoTracking()
                .Where(x => x.CourseId == courseId)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(
                cancellationToken);
        }

        public void Remove(CourseSection section)
        {
            _context.CourseSections.Remove(section);
        }
    }
}
