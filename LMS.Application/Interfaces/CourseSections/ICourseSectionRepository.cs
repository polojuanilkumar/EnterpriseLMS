using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.CourseSections
{
    public interface ICourseSectionRepository
    {
        Task AddAsync(
            CourseSection section,
            CancellationToken cancellationToken = default);

        Task<CourseSection?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CourseSection>> GetByCourseIdAsync(
            Guid courseId,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);

        void Remove(CourseSection section);
    }
}
