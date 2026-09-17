using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Enrollments
{
    public interface ICourseEnrollmentRepository
    {
        Task AddAsync(
            CourseEnrollment enrollment,
            CancellationToken cancellationToken = default);

        Task<CourseEnrollment?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<CourseEnrollment?> GetByCourseAndUserAsync(
            Guid courseId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CourseEnrollment>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
