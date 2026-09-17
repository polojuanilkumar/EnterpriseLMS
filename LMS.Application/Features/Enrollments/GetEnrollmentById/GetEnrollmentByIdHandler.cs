using LMS.Application.Interfaces.Enrollments;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.GetEnrollmentById
{
    public sealed class GetEnrollmentByIdHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;

        public GetEnrollmentByIdHandler(
            ICourseEnrollmentRepository enrollmentRepository)
        {
            _enrollmentRepository = enrollmentRepository;
        }

        public async Task<GetEnrollmentByIdResponse> HandleAsync(
            Guid enrollmentId,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            var enrollment =
                await _enrollmentRepository.GetByIdAsync(
                    enrollmentId,
                    cancellationToken);

            if (enrollment is null)
            {
                throw new KeyNotFoundException(
                    "Enrollment not found.");
            }

            if (enrollment.UserId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to access this enrollment.");
            }

            return new GetEnrollmentByIdResponse
            {
                Id = enrollment.Id,
                CourseId = enrollment.CourseId,
                UserId = enrollment.UserId,
                EnrolledAt = enrollment.EnrolledAt,
                CompletedAt = enrollment.CompletedAt,
                Status = enrollment.Status,
                ProgressPercentage = enrollment.ProgressPercentage
            };
        }
    }
}
