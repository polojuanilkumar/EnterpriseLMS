using LMS.Application.Interfaces.Courses;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.Lessons.Common
{
    public sealed class EnsureLessonReadable
    {
        private readonly OrganizationContext? _organization;
        private readonly ICourseRepository _courses;
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseEnrollmentRepository _enrollments;

        public EnsureLessonReadable(ICourseRepository courses, ICourseSectionRepository sections,
            ICourseEnrollmentRepository enrollments, OrganizationContext? organization = null)
        {
            _organization = organization;
            _courses = courses;
            _sections = sections;
            _enrollments = enrollments;
        }

        public Task CheckAsync(Lesson lesson, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default)
            => CheckLessonCoreAsync(lesson, userId, canViewUnpublished, allowCompleted: true, cancellationToken);

        // Learner mutations never inherit staff bypass or completed-enrollment read access.
        public Task CheckForMutationAsync(Lesson lesson, Guid userId,
            CancellationToken cancellationToken = default)
            => CheckLessonCoreAsync(lesson, userId, canViewUnpublished: false, allowCompleted: false, cancellationToken);

        private async Task CheckLessonCoreAsync(Lesson lesson, Guid userId, bool canViewUnpublished,
            bool allowCompleted, CancellationToken cancellationToken)
        {
            if (!canViewUnpublished && !lesson.IsPublished)
                throw new KeyNotFoundException("Lesson not found.");

            var section = await _sections.GetByIdAsync(lesson.SectionId, cancellationToken)
                ?? throw new KeyNotFoundException("Course section not found.");
            if (section.Id != lesson.SectionId) throw new KeyNotFoundException("Course section not found.");
            await CheckCourseCoreAsync(section.CourseId, userId, canViewUnpublished, allowCompleted, cancellationToken);
        }

        // Return the enrollment already validated by this check; staff bypass returns null.
        public Task<CourseEnrollment?> CheckCourseAsync(Guid courseId, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default)
            => CheckCourseCoreAsync(courseId, userId, canViewUnpublished, allowCompleted: true, cancellationToken);

        private async Task<CourseEnrollment?> CheckCourseCoreAsync(Guid courseId, Guid userId, bool canViewUnpublished,
            bool allowCompleted, CancellationToken cancellationToken)
        {
            var course = await _courses.GetByIdAsync(courseId, cancellationToken);
            if (course is null || course.Id != courseId) throw new KeyNotFoundException("Course not found.");
            if (canViewUnpublished)
            {
                if (_organization is null || !_organization.CanManageContent || _organization.UserId != userId)
                    throw new UnauthorizedAccessException("Verified staff membership is required.");
                if (course.OrganizationId != _organization.OrganizationId || !_organization.OrganizationId.HasValue)
                    throw new KeyNotFoundException("Course not found.");
                if (!_organization.IsSuperAdmin && _organization.Role == OrganizationRole.Instructor && course.InstructorId != userId)
                    throw new UnauthorizedAccessException("Course ownership is required.");
                return null;
            }
            if (!course.IsPublished)
                throw new KeyNotFoundException("Course not found.");

            if (userId == Guid.Empty)
                throw new UnauthorizedAccessException("Authenticated user ID was not found.");

            var enrollment = await _enrollments.GetByCourseAndUserAsync(course.Id, userId, cancellationToken);
            if (enrollment is null || (enrollment.Status != EnrollmentStatus.Active
                && !(allowCompleted && enrollment.Status == EnrollmentStatus.Completed)))
                throw new UnauthorizedAccessException(allowCompleted
                    ? "An active or completed course enrollment is required."
                    : "An active course enrollment is required.");

            return enrollment;
        }
    }
}
