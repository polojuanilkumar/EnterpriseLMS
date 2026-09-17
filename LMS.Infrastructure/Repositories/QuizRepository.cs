using LMS.Application.Interfaces.Quizzes;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class QuizRepository : IQuizRepository
    {
        private readonly LMSDbContext _context;

        public QuizRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(
            Quiz quiz,
            CancellationToken cancellationToken = default)
        {
            await _context.Quizzes.AddAsync(
                quiz,
                cancellationToken);
        }

        public async Task<Quiz?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _context.Quizzes
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<Quiz?> GetByLessonIdAsync(
            Guid lessonId,
            CancellationToken cancellationToken = default)
        {
            return await _context.Quizzes
                .FirstOrDefaultAsync(
                    x => x.LessonId == lessonId,
                    cancellationToken);
        }

        public async Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public void Remove(Quiz quiz)
        {
            _context.Quizzes.Remove(quiz);
        }
    }
}
