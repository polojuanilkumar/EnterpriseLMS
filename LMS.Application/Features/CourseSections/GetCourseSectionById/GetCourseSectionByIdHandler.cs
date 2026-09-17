using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections.GetCourseSectionById
{
    public sealed class GetCourseSectionByIdHandler
    {
        private readonly ICourseSectionRepository _sectionRepository;
        private readonly ICourseRepository _courseRepository;

        public GetCourseSectionByIdHandler(
            ICourseSectionRepository sectionRepository,
            ICourseRepository courseRepository)
        {
            _sectionRepository = sectionRepository;
            _courseRepository = courseRepository;
        }

        public async Task<CourseSectionResponse> HandleAsync(
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

            return new CourseSectionResponse
            {
                Id = section.Id,
                CourseId = section.CourseId,
                Title = section.Title,
                Description = section.Description,
                DisplayOrder = section.DisplayOrder,
                CreatedAt = section.CreatedAt,
                UpdatedAt = section.UpdatedAt
            };
        }
    }
}
