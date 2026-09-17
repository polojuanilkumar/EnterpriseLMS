using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace LMS.Infrastructure.Repositories
{
    public sealed class LessonRepository
        : ILessonRepository
    {
        private readonly LMSDbContext _context;

        public LessonRepository(
            LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            Lesson lesson,
            CancellationToken cancellationToken = default)
        {
            await _context.Lessons.AddAsync(
                lesson,
                cancellationToken);
        }

        public async Task<Lesson?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.Lessons
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<Lesson>> GetBySectionIdAsync(
            Guid sectionId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Lessons
                .AsNoTracking()
                .Where(x => x.SectionId == sectionId)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public void Remove(Lesson lesson)
        {
            _context.Lessons.Remove(lesson);
        }

        public async Task<IReadOnlyList<Lesson>> GetByCourseIdAsync(
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Lessons
                .Where(x => _context.CourseSections
                    .Any(s => s.Id == x.SectionId &&
                             s.CourseId == courseId))
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(cancellationToken);
        }
    }
}
