using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.PublishCourse
{
    public sealed class PublishCourseHandler
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PublishCourseHandler(
            ICourseRepository courseRepository,
            IUnitOfWork unitOfWork)
        {
            _courseRepository = courseRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            Guid courseId,
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

            // Instructor can publish only their own course.
            // Admin/SuperAdmin can publish any course.
            if (!isAdmin &&
                course.InstructorId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to publish this course.");
            }

            course.Publish();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
