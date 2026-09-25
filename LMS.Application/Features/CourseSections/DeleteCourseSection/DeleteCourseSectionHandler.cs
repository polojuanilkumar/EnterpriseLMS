using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Persistence;
using LMS.Application.Features.Quizzes.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections.DeleteCourseSection
{
    public sealed class DeleteCourseSectionHandler
    {
        private readonly ICourseSectionRepository _sectionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly EnsureQuizEditable _ensureQuizEditable;

        public DeleteCourseSectionHandler(
            ICourseSectionRepository sectionRepository,
            ICourseRepository courseRepository,
            IUnitOfWork unitOfWork, EnsureQuizEditable ensureQuizEditable)
        {
            _sectionRepository = sectionRepository;
            _courseRepository = courseRepository;
            _unitOfWork = unitOfWork;
            _ensureQuizEditable = ensureQuizEditable;
        }

        public Task HandleAsync(Guid courseId, Guid sectionId, CancellationToken cancellationToken = default)
            => _ensureQuizEditable.ExecuteParentDeletionAsync(sectionId, true,
                token => HandleCoreAsync(courseId, sectionId, token), cancellationToken);

        private async Task HandleCoreAsync(
            Guid courseId,
            Guid sectionId,
            CancellationToken cancellationToken = default)
        {
            var course =
                await _courseRepository.GetByIdAsync(
                    courseId,
                    cancellationToken);

            if (course is null)
            {
                throw new KeyNotFoundException(
                    "Course not found.");
            }

            var section =
                await _sectionRepository.GetByIdAsync(
                    sectionId,
                    cancellationToken);

            if (section is null ||
                section.CourseId != courseId)
            {
                throw new KeyNotFoundException(
                    "Course section not found.");
            }

            _sectionRepository.Remove(section);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
