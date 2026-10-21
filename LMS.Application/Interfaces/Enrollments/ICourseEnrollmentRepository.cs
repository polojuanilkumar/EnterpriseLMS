using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Enrollments
{
    public interface ICourseEnrollmentRepository
    {
        // Serialize cancellation and completion: load, validate, and save under the same row lock.
        Task ExecuteEnrollmentTransactionAsync(Guid enrollmentId,
            Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

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
