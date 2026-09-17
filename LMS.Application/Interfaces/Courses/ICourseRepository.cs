using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Courses
{
    public interface ICourseRepository
    {
        Task AddAsync(
            Course course,
            CancellationToken cancellationToken = default);

        Task<Course?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Course>> GetAllAsync(
            CancellationToken cancellationToken = default);

        Task<bool> ExistsByCodeAsync(
            string code,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
