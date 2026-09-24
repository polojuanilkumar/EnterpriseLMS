using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.QuizQuestions
{
    public interface IQuizQuestionRepository
    {
        Task AddAsync(
            QuizQuestion question,
            CancellationToken cancellationToken = default);

        Task<QuizQuestion?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<QuizQuestion>> GetByQuizIdAsync(
            Guid quizId,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);

        void Remove(QuizQuestion question);
    }
}
