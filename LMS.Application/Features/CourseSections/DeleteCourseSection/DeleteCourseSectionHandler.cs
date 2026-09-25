using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Persistence;
using LMS.Application.Features.Quizzes.Common;
using LMS.Domain.Entities;
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

        public async Task HandleAsync(Guid courseId, Guid sectionId, Guid currentUserId,
            bool isAdmin, CancellationToken cancellationToken = default)
        {
            // Check access before quiz-attempt guards can return a conflict.
            await GetAuthorizedSectionAsync(courseId, sectionId, currentUserId, isAdmin, cancellationToken);

            await _ensureQuizEditable.ExecuteParentDeletionAsync(sectionId, true,
                async token =>
                {
                    var section = await GetAuthorizedSectionAsync(
                        courseId, sectionId, currentUserId, isAdmin, token);
                    _sectionRepository.Remove(section);
                    await _unitOfWork.SaveChangesAsync(token);
                }, cancellationToken);
        }

        private async Task<CourseSection> GetAuthorizedSectionAsync(
            Guid courseId,
            Guid sectionId,
            Guid currentUserId,
            bool isAdmin,
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

            if (!isAdmin && course.InstructorId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to delete sections in this course.");
            }

            return section;
        }
    }
}
