using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.CompleteEnrollment
{
    public sealed class CompleteEnrollmentHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CompleteEnrollmentHandler(
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
                    "You are not authorized to complete this enrollment.");
            }

            enrollment.Complete();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
