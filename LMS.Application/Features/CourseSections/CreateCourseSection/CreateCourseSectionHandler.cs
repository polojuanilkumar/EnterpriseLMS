using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.CourseSections;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.CourseSections.CreateCourseSection
{
    public sealed class CreateCourseSectionHandler
    {
        private readonly ICourseSectionRepository _sectionRepository;
        private readonly ICourseRepository _courseRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateCourseSectionHandler(
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
            CreateCourseSectionRequest request,
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

            if (!isAdmin && course.InstructorId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to create sections in this course.");
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

            var section = new CourseSection(
                courseId,
                request.Title.Trim(),
                request.Description?.Trim(),
                request.DisplayOrder);

            await _sectionRepository.AddAsync(
                section,
                cancellationToken);

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
