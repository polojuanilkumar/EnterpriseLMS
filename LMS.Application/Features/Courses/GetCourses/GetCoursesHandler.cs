using LMS.Application.Features.Courses.Common;
using LMS.Application.Interfaces.Courses;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.GetCourses
{
    public sealed class GetCoursesHandler
    {
        private readonly ICourseRepository _courseRepository;

        public GetCoursesHandler(
            ICourseRepository courseRepository)
        {
            _courseRepository = courseRepository;
        }

        public async Task<IReadOnlyList<CourseResponse>> HandleAsync(
            CancellationToken cancellationToken = default)
        {
            var courses =
                await _courseRepository.GetAllAsync(
                    cancellationToken);

            return courses
                .Select(course => new CourseResponse
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
                })
                .ToList();
        }
    }
}
