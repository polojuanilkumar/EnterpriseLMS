using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections.UpdateCourseSection
{
    public sealed class UpdateCourseSectionHandler
    {
        private readonly ICourseSectionRepository _sectionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateCourseSectionHandler(
            ICourseSectionRepository sectionRepository,
            ICourseRepository courseRepository,
            IUnitOfWork unitOfWork)
        {
            _sectionRepository = sectionRepository;
            _courseRepository = courseRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<CourseSectionResponse> HandleAsync(
            Guid courseId,
            Guid sectionId,
            UpdateCourseSectionRequest request,
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

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException(
                    "Section title is required.");
            }

            if (request.DisplayOrder < 1)
            {
                throw new ArgumentException(
                    "Display order must be greater than 0.");
            }

            section.Update(
                request.Title.Trim(),
                request.Description?.Trim(),
                request.DisplayOrder);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

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
