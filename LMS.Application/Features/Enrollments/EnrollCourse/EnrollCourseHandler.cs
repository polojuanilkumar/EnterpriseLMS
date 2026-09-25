using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Enrollments.EnrollCourse
{
    public sealed class EnrollCourseHandler
    {
        private readonly ICourseEnrollmentRepository _enrollmentRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;

        public EnrollCourseHandler(
            ICourseEnrollmentRepository enrollmentRepository,
            ICourseRepository courseRepository,
            IIdentityService identityService,
            IUnitOfWork unitOfWork)
        {
            _enrollmentRepository = enrollmentRepository;
            _courseRepository = courseRepository;
            _identityService = identityService;
            _unitOfWork = unitOfWork;
        }

        public async Task<Guid> HandleAsync(
            EnrollCourseRequest request,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            var isStudent =
                await _identityService.IsInRoleAsync(
                    currentUserId,
                    "Student");

            if (!isStudent)
            {
                throw new UnauthorizedAccessException(
                    "Only students can enroll in courses.");
            }

            var course =
                await _courseRepository.GetByIdAsync(
                    request.CourseId,
                    cancellationToken);

            if (course is null)
            {
                throw new KeyNotFoundException(
                    "Course not found.");
            }

            if (course.Status != CourseStatus.Published ||
                !course.IsPublished)
            {
                throw new InvalidOperationException(
                    "Only published courses can be enrolled.");
            }

            var existingEnrollment =
                await _enrollmentRepository.GetByCourseAndUserAsync(
                    request.CourseId,
                    currentUserId,
                    cancellationToken);

            if (existingEnrollment is not null)
            {
                if (existingEnrollment.Status == EnrollmentStatus.Cancelled)
                {
                    existingEnrollment.Reactivate();

                    await _enrollmentRepository.SaveChangesAsync(
                        cancellationToken);

                    return existingEnrollment.Id;
                }

                throw new InvalidOperationException(
                    "Student is already enrolled in this course.");
            }

            var enrollment = new CourseEnrollment(
                request.CourseId,
                currentUserId);

            await _enrollmentRepository.AddAsync(
                enrollment,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

            return enrollment.Id;
        }
    }
}
