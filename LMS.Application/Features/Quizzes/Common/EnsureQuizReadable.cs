using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Enrollments;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class EnsureQuizReadable
    {
        private readonly ILessonRepository _lessons;
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseEnrollmentRepository _enrollments;

        public EnsureQuizReadable(ILessonRepository lessons, ICourseSectionRepository sections,
            ICourseEnrollmentRepository enrollments)
        {
            _lessons = lessons;
            _sections = sections;
            _enrollments = enrollments;
        }

        public async Task CheckAsync(Quiz? quiz, Guid userId, bool canViewUnpublished,
            CancellationToken cancellationToken = default, string notFoundMessage = "Quiz not found.")
        {
            if (quiz is null || (!quiz.IsPublished && !canViewUnpublished))
                throw new KeyNotFoundException(notFoundMessage);

            if (canViewUnpublished)
                return;

            if (userId == Guid.Empty)
                throw new UnauthorizedAccessException("Authenticated user ID was not found.");

            var lesson = await _lessons.GetByIdAsync(quiz.LessonId, cancellationToken)
                ?? throw new KeyNotFoundException("Lesson not found.");
            var section = await _sections.GetByIdAsync(lesson.SectionId, cancellationToken)
                ?? throw new KeyNotFoundException("Course section not found.");
            var enrollment = await _enrollments.GetByCourseAndUserAsync(
                section.CourseId, userId, cancellationToken);
            if (enrollment is null || enrollment.Status != EnrollmentStatus.Active)
                throw new UnauthorizedAccessException("An active course enrollment is required.");
        }
    }
}
