using LMS.Application.Interfaces.QuizOptions;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class QuizOptionRepository : IQuizOptionRepository
    {
        private readonly LMSDbContext _context;

        public QuizOptionRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            QuizOption option,
            CancellationToken cancellationToken = default)
        {
            await _context.QuizOptions.AddAsync(
                option,
                cancellationToken);
        }

        public async Task<QuizOption?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.QuizOptions
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<QuizOption>> GetByQuestionIdAsync(
            Guid questionId,
            CancellationToken cancellationToken = default)
        {
            return await _context.QuizOptions
                .Where(x => x.QuestionId == questionId)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        public void Remove(QuizOption option)
        {
            _context.QuizOptions.Remove(option);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
