using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.LessonProgresses
{
    public interface ILessonProgressRepository
    {
        Task AddAsync(
            LessonProgress progress,
            CancellationToken cancellationToken = default);

        Task<LessonProgress?> GetByUserAndLessonAsync(
            Guid userId,
            Guid lessonId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<LessonProgress>> GetByUserAndLessonIdsAsync(
    Guid userId,
    IReadOnlyList<Guid> lessonIds,
    CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
