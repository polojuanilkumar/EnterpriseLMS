using LMS.Application.Features.Courses.Common;
using LMS.Application.Interfaces.Courses;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.GetCourseById
{
    public sealed class GetCourseByIdHandler
    {
        private readonly ICourseRepository _courseRepository;

        public GetCourseByIdHandler(
            ICourseRepository courseRepository)
        {
            _courseRepository = courseRepository;
        }

        public async Task<CourseResponse?> HandleAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var course =
                await _courseRepository.GetByIdAsync(
                    id,
                    cancellationToken);

            if (course is null)
            {
                return null;
            }

            return new CourseResponse
            {
                Id = course.Id,
                Title = course.Title,
                Code = course.Code,
                Description = course.Description,
                CategoryId = course.CategoryId,
                CategoryName = course.Category?.Name,
                InstructorId = course.InstructorId,
                Level = course.Level.ToString(),
                Status = course.Status.ToString(),
                DurationInMinutes = course.DurationInMinutes,
                ThumbnailUrl = course.ThumbnailUrl,
                IsPublished = course.IsPublished,
                CreatedAt = course.CreatedAt,
                UpdatedAt = course.UpdatedAt
            };
        }
    }
}
