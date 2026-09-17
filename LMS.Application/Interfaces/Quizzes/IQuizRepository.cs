using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Quizzes
{
    public interface IQuizRepository
    {
        Task AddAsync(
            Quiz quiz,
            CancellationToken cancellationToken = default);

        Task<Quiz?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<Quiz?> GetByLessonIdAsync(
            Guid lessonId,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);

        void Remove(Quiz quiz);
    }
}
