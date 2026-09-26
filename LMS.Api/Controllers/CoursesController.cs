using LMS.Application.Common.Models;
using LMS.Application.Features.CourseProgress.Common;
using LMS.Application.Features.CourseProgress.GetProgress;
using LMS.Application.Features.Courses.ArchiveCourse;
using LMS.Application.Features.Courses.CreateCourse;
using LMS.Application.Features.Courses.GetCourseById;
using LMS.Application.Features.Courses.GetCourses;
using LMS.Application.Features.Courses.PublishCourse;
using LMS.Application.Features.Courses.UpdateCourse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public sealed class CoursesController : ControllerBase
    {
        private readonly CreateCourseHandler _createCourseHandler;
        private readonly GetCoursesHandler _getCoursesHandler;
        private readonly GetCourseByIdHandler _getCourseByIdHandler;

        private readonly UpdateCourseHandler _updateCourseHandler;

        private readonly PublishCourseHandler _publishCourseHandler;

        private readonly ArchiveCourseHandler _archiveCourseHandler;

        private readonly GetCourseProgressHandler _getCourseProgressHandler;

        public CoursesController(
            CreateCourseHandler createCourseHandler,
            GetCoursesHandler getCoursesHandler,
            GetCourseByIdHandler getCourseByIdHandler,
            UpdateCourseHandler updateCourseHandler, PublishCourseHandler publishCourseHandler, ArchiveCourseHandler archiveCourseHandler, GetCourseProgressHandler getCourseProgressHandler)
        {
            _createCourseHandler = createCourseHandler;
            _getCoursesHandler = getCoursesHandler;
            _getCourseByIdHandler = getCourseByIdHandler;
            _updateCourseHandler = updateCourseHandler;
            _publishCourseHandler = publishCourseHandler;
            _archiveCourseHandler = archiveCourseHandler;
            _getCourseProgressHandler = getCourseProgressHandler;

        }

        [HttpPost]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCourse(
            [FromBody] CreateCourseRequest request,
            CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(
                    userIdValue,
                    out var currentUserId))
            {
                return Unauthorized();
            }

            var isAdmin =
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            try
            {
                var courseId =
                    await _createCourseHandler.HandleAsync(
                        request,
                        currentUserId,
                        isAdmin,
                        cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        Success = true,
                        Message = "Course created successfully.",
                        Data = new
                        {
                            CourseId = courseId
                        }
                    });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetCourses(
    CancellationToken cancellationToken)
        {
            var courses =
                await _getCoursesHandler.HandleAsync(
                    cancellationToken);

            return Ok(new
            {
                Success = true,
                Message = "Courses retrieved successfully.",
                Data = courses
            });
        }


        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCourseById(
    Guid id,
    CancellationToken cancellationToken)
        {
            var course =
                await _getCourseByIdHandler.HandleAsync(
                    id,
                    cancellationToken);

            if (course is null)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = "Course not found."
                });
            }

            return Ok(new
            {
                Success = true,
                Message = "Course retrieved successfully.",
                Data = course
            });
        }



        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCourse(
           Guid id,
           [FromBody] UpdateCourseRequest request,
           CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(
                    userIdValue,
                    out var currentUserId))
            {
                return Unauthorized();
            }

            var isAdmin =
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            try
            {
                await _updateCourseHandler.HandleAsync(
                    id,
                    request,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    Success = true,
                    Message = "Course updated successfully."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }




        [HttpPost("{id:guid}/publish")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishCourse(
           Guid id,
           CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdValue, out var currentUserId))
            {
                return Unauthorized();
            }

            var isAdmin =
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            try
            {
                await _publishCourseHandler.HandleAsync(
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    Success = true,
                    Message = "Course published successfully."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }



        [HttpPost("{id:guid}/archive")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> ArchiveCourse(
    Guid id,
    CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdValue, out var currentUserId))
            {
                return Unauthorized();
            }

            var isAdmin =
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            try
            {
                await _archiveCourseHandler.HandleAsync(
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    Success = true,
                    Message = "Course archived successfully."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }



        [Authorize(Roles = "Student")]
        [HttpGet("/api/Courses/{id:guid}/progress")]
        [ProducesResponseType(typeof(ApiResponse<CourseProgressResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetProgress(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Authenticated user ID was not found."
                });
            }

            try
            {
                var result = await _getCourseProgressHandler.HandleAsync(
                    id,
                    userId,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Course progress retrieved successfully.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }






































    }
}
