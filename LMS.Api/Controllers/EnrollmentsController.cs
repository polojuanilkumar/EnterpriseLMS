using LMS.Application.Common.Models;
using LMS.Application.Features.Enrollments.CancelEnrollment;
using LMS.Application.Features.Enrollments.CompleteEnrollment;
using LMS.Application.Features.Enrollments.EnrollCourse;
using LMS.Application.Features.Enrollments.GetEnrollmentById;
using LMS.Application.Features.Enrollments.GetMyEnrollments;
using LMS.Application.Features.CourseProgress.MyCourses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Route("api/Enrollments")]
    [Authorize]
    public sealed class EnrollmentsController : ControllerBase
    {
        private readonly EnrollCourseHandler _enrollCourseHandler;

        private readonly GetMyEnrollmentsHandler _getMyEnrollmentsHandler;
        private readonly GetEnrollmentByIdHandler _getEnrollmentByIdHandler;
        private readonly CompleteEnrollmentHandler _completeEnrollmentHandler;

        private readonly CancelEnrollmentHandler _cancelEnrollmentHandler;

        private readonly GetMyCoursesProgressHandler _getMyCoursesProgressHandler;

        public EnrollmentsController(
            EnrollCourseHandler enrollCourseHandler, GetMyEnrollmentsHandler getMyEnrollmentsHandler, GetEnrollmentByIdHandler getEnrollmentByIdHandler, CompleteEnrollmentHandler completeEnrollmentHandler, CancelEnrollmentHandler cancelEnrollmentHandler, GetMyCoursesProgressHandler getMyCoursesProgressHandler)
        {
            _enrollCourseHandler = enrollCourseHandler;
            _getMyEnrollmentsHandler = getMyEnrollmentsHandler;
            _getEnrollmentByIdHandler = getEnrollmentByIdHandler;
            _completeEnrollmentHandler = completeEnrollmentHandler;
            _cancelEnrollmentHandler = cancelEnrollmentHandler;
            _getMyCoursesProgressHandler = getMyCoursesProgressHandler;
        }

        [HttpPost("/api/Courses/{courseId}/enroll")]
        public async Task<IActionResult> Enroll(
            Guid courseId,
            CancellationToken cancellationToken)
        {
            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                return Unauthorized();
            }

            var request = new EnrollCourseRequest
            {
                CourseId = courseId
            };

            var enrollmentId =
                await _enrollCourseHandler.HandleAsync(
                    request,
                    currentUserId,
                    cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    success = true,
                    message = "Course enrolled successfully.",
                    data = new
                    {
                        enrollmentId
                    }
                });
        }



        [HttpGet("me")]
        public async Task<IActionResult> GetMyEnrollments(
    CancellationToken cancellationToken)
        {
            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                return Unauthorized();
            }

            var enrollments =
                await _getMyEnrollmentsHandler.HandleAsync(
                    currentUserId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Enrollments retrieved successfully.",
                data = enrollments
            });
        }




        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(
    Guid id,
    CancellationToken cancellationToken)
        {
            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                return Unauthorized();
            }

            var enrollment =
                await _getEnrollmentByIdHandler.HandleAsync(
                    id,
                    currentUserId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Enrollment retrieved successfully.",
                data = enrollment
            });
        }


        [Authorize(Roles = "Student")]
        [HttpPatch("{id:guid}/complete")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Complete(
    Guid id,
    CancellationToken cancellationToken)
        {
            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var currentUserId) || currentUserId == Guid.Empty)
            {
                return Unauthorized();
            }

            try
            {
                await _completeEnrollmentHandler.HandleAsync(
                    id,
                    currentUserId,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Enrollment completed successfully."
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }



        [HttpPatch("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(
    Guid id,
    CancellationToken cancellationToken)
        {
            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var currentUserId))
            {
                return Unauthorized();
            }

            await _cancelEnrollmentHandler.HandleAsync(
                id,
                currentUserId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Enrollment cancelled successfully."
            });
        }




        [Authorize(Roles = "Student")]
        [HttpGet("my-courses/progress")]
        public async Task<IActionResult> GetMyCoursesProgress(
    CancellationToken cancellationToken)
        {
            var email = User.FindFirst(
                System.Security.Claims.ClaimTypes.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "Authenticated user email was not found."
                });
            }

            var result = await _getMyCoursesProgressHandler.HandleAsync(
                email,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "My course progress retrieved successfully.",
                data = result
            });
        }









    }
}
