using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.QuizOptions
{
    public interface IQuizOptionRepository
    {
        Task AddAsync(
            QuizOption option,
            CancellationToken cancellationToken = default);

        Task<QuizOption?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<QuizOption>> GetByQuestionIdAsync(
            Guid questionId,
            CancellationToken cancellationToken = default);

        void Remove(QuizOption option);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
