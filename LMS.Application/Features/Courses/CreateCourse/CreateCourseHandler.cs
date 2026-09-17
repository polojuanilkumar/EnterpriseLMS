using LMS.Application.Interfaces.Categories;
using LMS.Application.Interfaces.Courses;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Courses.CreateCourse
{
    public sealed class CreateCourseHandler
    {
        private readonly ICourseRepository _courseRepository;
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;

        private readonly ICategoryRepository _categoryRepository;

        public CreateCourseHandler(
            ICourseRepository courseRepository,
            IIdentityService identityService,
            IUnitOfWork unitOfWork, ICategoryRepository categoryRepository)
        {
            _courseRepository = courseRepository;
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _categoryRepository = categoryRepository;
        }

        public async Task<Guid> HandleAsync(
            CreateCourseRequest request,
            Guid currentUserId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            var codeExists =
                await _courseRepository.ExistsByCodeAsync(
                    request.Code,
                    cancellationToken);

            if (codeExists)
            {
                throw new InvalidOperationException(
                    "A course with this code already exists.");
            }

            var instructorId = request.InstructorId;

            if (!isAdmin)
            {
                instructorId = currentUserId;

                var isInstructor =
                    await _identityService.IsInRoleAsync(
                        currentUserId,
                        "Instructor");

                if (!isInstructor)
                {
                    throw new UnauthorizedAccessException(
                        "Only instructors can create courses.");
                }
            }

            if (instructorId is null)
            {
                throw new InvalidOperationException(
                    "InstructorId is required.");
            }

            var instructorExists =
                await _identityService.UserExistsAsync(
                    instructorId.Value);

            if (!instructorExists)
            {
                throw new InvalidOperationException(
                    "Instructor does not exist.");
            }

            var isValidInstructor =
                await _identityService.IsInRoleAsync(
                    instructorId.Value,
                    "Instructor");

            if (!isValidInstructor)
            {
                throw new InvalidOperationException(
                    "Selected user is not an instructor.");
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



            var course = new Course(
                request.Title,
                request.Code,
                request.Description,
                instructorId.Value,
                request.Level);

            course.SetCategory(request.CategoryId);

            course.Update(
                request.Title,
                request.Code,
                request.Description,
                request.Level,
                request.DurationInMinutes,
                request.ThumbnailUrl);

            await _courseRepository.AddAsync(
                course,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

            return course.Id;
        }
    }
}
