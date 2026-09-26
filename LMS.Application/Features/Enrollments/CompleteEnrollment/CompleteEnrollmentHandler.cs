using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.LessonProgresses;
using LMS.Domain.Enums;
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
        private readonly ICourseRepository _courses;
        private readonly ILessonRepository _lessons;
        private readonly ILessonProgressRepository _progress;

        public CompleteEnrollmentHandler(
            ICourseEnrollmentRepository enrollmentRepository,
            IUnitOfWork unitOfWork,
            ICourseRepository courses,
            ILessonRepository lessons,
            ILessonProgressRepository progress)
        {
            _enrollmentRepository = enrollmentRepository;
            _unitOfWork = unitOfWork;
            _courses = courses;
            _lessons = lessons;
            _progress = progress;
        }

        public Task HandleAsync(
            Guid enrollmentId,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
            => _enrollmentRepository.ExecuteCompletionTransactionAsync(enrollmentId,
                token => HandleCoreAsync(enrollmentId, currentUserId, token), cancellationToken);

        private async Task HandleCoreAsync(Guid enrollmentId, Guid currentUserId,
            CancellationToken cancellationToken)
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

            if (currentUserId == Guid.Empty || enrollment.UserId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to complete this enrollment.");
            }

            var course = await _courses.GetByIdAsync(enrollment.CourseId, cancellationToken);
            if (course is null || !course.IsPublished)
                throw new KeyNotFoundException("Course not found.");

            if (enrollment.Status != EnrollmentStatus.Active)
                throw new InvalidOperationException("Only active enrollments can be completed.");

            var lessons = await _lessons.GetByCourseIdAsync(course.Id, cancellationToken);
            var publishedLessonIds = lessons.Where(x => x.IsPublished)
                .Select(x => x.Id).Distinct().ToList();
            if (publishedLessonIds.Count == 0)
                throw new InvalidOperationException("The course must have at least one published lesson before completion.");

            var progress = await _progress.GetByUserAndLessonIdsAsync(
                currentUserId, publishedLessonIds, cancellationToken);
            var completedLessonIds = progress
                .Where(x => x.UserId == currentUserId && x.IsCompleted)
                .Select(x => x.LessonId).ToHashSet();
            if (!publishedLessonIds.All(completedLessonIds.Contains))
                throw new InvalidOperationException("Complete every published lesson before completing the enrollment.");

            enrollment.Complete();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
