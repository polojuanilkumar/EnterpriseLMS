using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Courses;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.UpdateCourse
{
    public sealed class UpdateCourseHandler
    {
        private readonly ICourseRepository _courseRepository;

        private readonly ICategoryRepository _categoryRepository;

        public UpdateCourseHandler(
            ICourseRepository courseRepository, ICategoryRepository categoryRepository)
        {
            _courseRepository = courseRepository;
            _categoryRepository = categoryRepository;
        }

        public async Task HandleAsync(
            Guid courseId,
            UpdateCourseRequest request,
            Guid currentUserId,
            bool isAdmin,
            CancellationToken cancellationToken)
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

            if (!isAdmin &&
                course.InstructorId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to update this course.");
            }

            if (request.CategoryId.HasValue)
            {
                var category =
                    await _categoryRepository.GetByIdAsync(
                        request.CategoryId.Value,
                        cancellationToken);

                if (category is null)
                {
                    throw new InvalidOperationException(
                        "Category does not exist.");
                }

                if (!category.IsActive)
                {
                    throw new InvalidOperationException(
                        "Category is inactive.");
                }
            }

            course.Update(
                request.Title,
                request.Code,
                request.Description,
                request.Level,
                request.DurationInMinutes,
                request.ThumbnailUrl);

            course.SetCategory(request.CategoryId);

            await _courseRepository.SaveChangesAsync(
                cancellationToken);
        }
    }
}
