using LMS.Application.Interfaces.LessonProgresses;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class LessonProgressRepository
        : ILessonProgressRepository
    {
        private readonly LMSDbContext _context;

        public LessonProgressRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            LessonProgress progress,
            CancellationToken cancellationToken = default)
        {
            await _context.LessonProgresses.AddAsync(
                progress,
                cancellationToken);
        }

        public async Task<LessonProgress?> GetByUserAndLessonAsync(
            Guid userId,
            Guid lessonId,
            CancellationToken cancellationToken = default)
        {
            return await _context.LessonProgresses
                .FirstOrDefaultAsync(
                    x => x.UserId == userId &&
                         x.LessonId == lessonId,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<LessonProgress>> GetByUserAndLessonIdsAsync(
    Guid userId,
    IReadOnlyList<Guid> lessonIds,
    CancellationToken cancellationToken = default)
        {
            return await _context.LessonProgresses
                .Where(x => x.UserId == userId &&
                            lessonIds.Contains(x.LessonId))
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }


    }
}
