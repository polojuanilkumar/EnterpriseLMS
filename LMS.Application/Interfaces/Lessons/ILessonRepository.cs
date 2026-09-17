using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Lessons
{
    public interface ILessonRepository
    {
        Task AddAsync(
            Lesson lesson,
            CancellationToken cancellationToken = default);

        Task<Lesson?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Lesson>> GetBySectionIdAsync(
            Guid sectionId,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);

        void Remove(Lesson lesson);

        Task<IReadOnlyList<Lesson>> GetByCourseIdAsync(
    Guid courseId,
    CancellationToken cancellationToken = default);
    }
}
