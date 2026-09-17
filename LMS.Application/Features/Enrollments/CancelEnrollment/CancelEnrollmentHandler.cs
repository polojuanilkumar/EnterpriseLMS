using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.CancelEnrollment
{
    public sealed class CancelEnrollmentHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CancelEnrollmentHandler(
            ICourseEnrollmentRepository enrollmentRepository,
            IUnitOfWork unitOfWork)
        {
            _enrollmentRepository = enrollmentRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
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
                    "You are not authorized to cancel this enrollment.");
            }

            if (enrollment.Status != EnrollmentStatus.Active)
            {
                throw new InvalidOperationException(
                    "Only active enrollments can be cancelled.");
            }

            enrollment.Cancel();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
