using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.Lessons.Common
{
    public sealed class EnsureLessonReadable
    {
        private readonly ICourseRepository _courses;
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseEnrollmentRepository _enrollments;

        public EnsureLessonReadable(ICourseRepository courses, ICourseSectionRepository sections,
            ICourseEnrollmentRepository enrollments)
        {
            _courses = courses;
            _sections = sections;
            _enrollments = enrollments;
        }

        public async Task CheckAsync(Lesson lesson, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            if (canViewUnpublished)
                return;

            if (!lesson.IsPublished)
                throw new KeyNotFoundException("Lesson not found.");

            var section = await _sections.GetByIdAsync(lesson.SectionId, cancellationToken)
                ?? throw new KeyNotFoundException("Course section not found.");
            await CheckCourseAsync(section.CourseId, userId, canViewUnpublished, cancellationToken);
        }

        public async Task CheckCourseAsync(Guid courseId, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default)
        {
            if (canViewUnpublished)
                return;

            var course = await _courses.GetByIdAsync(courseId, cancellationToken);
            if (course is null || !course.IsPublished)
                throw new KeyNotFoundException("Course not found.");

            if (userId == Guid.Empty)
                throw new UnauthorizedAccessException("Authenticated user ID was not found.");

            var enrollment = await _enrollments.GetByCourseAndUserAsync(course.Id, userId, cancellationToken);
            if (enrollment is null || enrollment.Status != EnrollmentStatus.Active)
                throw new UnauthorizedAccessException("An active course enrollment is required.");
        }
    }
}
