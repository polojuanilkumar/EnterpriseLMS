using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;

namespace LMS.Application.Features.Lessons.Common
{
    public sealed class EnsureLessonOwnership
    {
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseRepository _courses;

        public EnsureLessonOwnership(ICourseSectionRepository sections, ICourseRepository courses)
        {
            _sections = sections;
            _courses = courses;
        }

        public async Task CheckSectionAsync(Guid sectionId, Guid currentUserId, bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            var section = await _sections.GetByIdAsync(sectionId, cancellationToken);
            if (section is null || section.Id != sectionId)
                throw new KeyNotFoundException("Course section not found.");

            var course = await _courses.GetByIdAsync(section.CourseId, cancellationToken);
            if (course is null || course.Id != section.CourseId)
                throw new KeyNotFoundException("Course not found.");

            if (!isAdmin && course.InstructorId != currentUserId)
                throw new UnauthorizedAccessException("You are not authorized to change lessons in this course.");
        }
    }
}
