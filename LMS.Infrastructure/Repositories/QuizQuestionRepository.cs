using LMS.Application.Interfaces.QuizQuestions;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class QuizQuestionRepository : IQuizQuestionRepository
    {
        private readonly LMSDbContext _context;

        public QuizQuestionRepository(
            LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            QuizQuestion question,
            CancellationToken cancellationToken = default)
        {
            await _context.QuizQuestions.AddAsync(
                question,
                cancellationToken);
        }

        public async Task<QuizQuestion?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.QuizQuestions
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<QuizQuestion>> GetByQuizIdAsync(
            Guid quizId,
            CancellationToken cancellationToken = default)
        {
            return await _context.QuizQuestions
                .Where(x => x.QuizId == quizId)
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public void Remove(QuizQuestion question)
        {
            _context.QuizQuestions.Remove(question);
        }
    }
}
