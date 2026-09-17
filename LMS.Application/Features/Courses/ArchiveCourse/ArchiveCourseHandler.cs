using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.ArchiveCourse
{
    public sealed class ArchiveCourseHandler
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IUnitOfWork _unitOfWork;

        public ArchiveCourseHandler(
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

            // Instructor can archive only their own course.
            // Admin/SuperAdmin can archive any course.
            if (!isAdmin &&
                course.InstructorId != currentUserId)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to archive this course.");
            }

            course.Archive();

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);
        }
    }
}
