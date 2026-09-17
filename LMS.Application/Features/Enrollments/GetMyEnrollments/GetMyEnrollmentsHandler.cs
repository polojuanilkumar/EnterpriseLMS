using LMS.Application.Interfaces.Enrollments;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.GetMyEnrollments
{
    public sealed class GetMyEnrollmentsHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;

        public GetMyEnrollmentsHandler(
            ICourseEnrollmentRepository enrollmentRepository)
        {
            _enrollmentRepository = enrollmentRepository;
        }

        public async Task<IReadOnlyList<GetMyEnrollmentsResponse>> HandleAsync(
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            var enrollments =
                await _enrollmentRepository.GetByUserIdAsync(
                    currentUserId,
                    cancellationToken);

            return enrollments
                .Select(x => new GetMyEnrollmentsResponse
                {
                    Id = x.Id,
                    CourseId = x.CourseId,
                    EnrolledAt = x.EnrolledAt,
                    CompletedAt = x.CompletedAt,
                    Status = x.Status,
                    ProgressPercentage = x.ProgressPercentage
                })
                .ToList();
        }
    }
}
