using LMS.Application.Common.Models;
using LMS.Application.Features.CourseSections;
using LMS.Application.Features.CourseSections.CreateCourseSection;
using LMS.Application.Features.CourseSections.DeleteCourseSection;
using LMS.Application.Features.CourseSections.GetCourseSectionById;
using LMS.Application.Features.CourseSections.GetCourseSections;
using LMS.Application.Features.CourseSections.UpdateCourseSection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Route("api/Courses/{courseId:guid}/sections")]
    [Authorize]
    public sealed class CourseSectionsController : ControllerBase
    {
        private readonly CreateCourseSectionHandler _createHandler;
        private readonly GetCourseSectionsHandler _getAllHandler;
        private readonly GetCourseSectionByIdHandler _getByIdHandler;
        private readonly UpdateCourseSectionHandler _updateHandler;
        private readonly DeleteCourseSectionHandler _deleteHandler;

        public CourseSectionsController(
            CreateCourseSectionHandler createHandler,
            GetCourseSectionsHandler getAllHandler,
            GetCourseSectionByIdHandler getByIdHandler,
            UpdateCourseSectionHandler updateHandler,
            DeleteCourseSectionHandler deleteHandler)
        {
            _createHandler = createHandler;
            _getAllHandler = getAllHandler;
            _getByIdHandler = getByIdHandler;
            _updateHandler = updateHandler;
            _deleteHandler = deleteHandler;
        }

        [HttpPost]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> Create(
            Guid courseId,
            CreateCourseSectionRequest request,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var section =
                    await _createHandler.HandleAsync(
                        courseId,
                        request,
                        currentUserId,
                        isAdmin,
                        cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Course section created successfully.",
                        data = section
                    });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CourseSectionResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid learner user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAll(
            Guid courseId,
            CancellationToken cancellationToken)
        {
            var canViewUnpublished = User.IsInRole("Instructor")
                || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            var hasUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                && userId != Guid.Empty;
            if (!canViewUnpublished && !hasUserId)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var sections =
                    await _getAllHandler.HandleAsync(
                        courseId,
                        userId,
                        canViewUnpublished,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Course sections retrieved successfully.",
                    data = sections
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<CourseSectionResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid learner user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(
            Guid courseId,
            Guid id,
            CancellationToken cancellationToken)
        {
            var canViewUnpublished = User.IsInRole("Instructor")
                || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            var hasUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                && userId != Guid.Empty;
            if (!canViewUnpublished && !hasUserId)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var section =
                    await _getByIdHandler.HandleAsync(
                        courseId,
                        id,
                        userId,
                        canViewUnpublished,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Course section retrieved successfully.",
                    data = section
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> Update(
            Guid courseId,
            Guid id,
            UpdateCourseSectionRequest request,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var section =
                    await _updateHandler.HandleAsync(
                        courseId,
                        id,
                        request,
                        currentUserId,
                        isAdmin,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Course section updated successfully.",
                    data = section
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(
            Guid courseId,
            Guid id,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _deleteHandler.HandleAsync(
                    courseId,
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Course section deleted successfully."
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}
