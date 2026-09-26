using LMS.Application.Features.Lessons.Common;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections.GetCourseSections
{
    public sealed class GetCourseSectionsHandler
    {
        private readonly ICourseSectionRepository _sectionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly EnsureLessonReadable _readable;

        public GetCourseSectionsHandler(
            ICourseSectionRepository sectionRepository,
            ICourseRepository courseRepository,
            EnsureLessonReadable readable)
        {
            _sectionRepository = sectionRepository;
            _courseRepository = courseRepository;
            _readable = readable;
        }

        public async Task<IReadOnlyList<CourseSectionResponse>> HandleAsync(
            Guid courseId,
            Guid userId,
            bool canViewUnpublished,
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

            await _readable.CheckCourseAsync(course.Id, userId, canViewUnpublished, cancellationToken);

            var sections =
                await _sectionRepository.GetByCourseIdAsync(
                    courseId,
                    cancellationToken);

            return sections
                .Select(x => new CourseSectionResponse
                {
                    Id = x.Id,
                    CourseId = x.CourseId,
                    Title = x.Title,
                    Description = x.Description,
                    DisplayOrder = x.DisplayOrder,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToList();
        }
    }
}
