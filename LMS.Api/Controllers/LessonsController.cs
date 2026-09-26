using LMS.Application.Features.Quizzes.Common;
using System.Security.Claims;
using LMS.Application.Common.Models;
﻿using LMS.Application.Features.LessonProgress.CompleteLesson;
using LMS.Application.Features.LessonProgress.GetProgress;
using LMS.Application.Features.LessonProgress.StartLesson;
using LMS.Application.Features.LessonProgress.UpdateProgress;
using LMS.Application.Features.Lessons.Common;
using LMS.Application.Features.Lessons.CreateLesson;
using LMS.Application.Features.Lessons.DeleteLesson;
using LMS.Application.Features.Lessons.GetLessonById;
using LMS.Application.Features.Lessons.GetLessons;
using LMS.Application.Features.Lessons.PublishLesson;
using LMS.Application.Features.Lessons.UnpublishLesson;
using LMS.Application.Features.Lessons.UpdateLesson;
using LMS.Application.Features.Quizzes.CreateQuiz;
using LMS.Application.Features.Quizzes.DeleteQuiz;
using LMS.Application.Features.Quizzes.GetQuiz;
using LMS.Application.Features.Quizzes.PublishQuiz;
using LMS.Application.Features.Quizzes.UnpublishQuiz;
using LMS.Application.Features.Quizzes.UpdateQuiz;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Authorize]
    public sealed class LessonsController : ControllerBase
    {
        private readonly CreateLessonHandler _createLessonHandler;
        private readonly GetLessonsHandler _getLessonsHandler;
        private readonly GetLessonByIdHandler _getLessonByIdHandler;
        private readonly UpdateLessonHandler _updateLessonHandler;
        private readonly DeleteLessonHandler _deleteLessonHandler;
        private readonly PublishLessonHandler _publishLessonHandler;
        private readonly UnpublishLessonHandler _unpublishLessonHandler;

        private readonly StartLessonHandler _startLessonHandler;
        private readonly UpdateLessonProgressHandler _updateLessonProgressHandler;
        private readonly CompleteLessonHandler _completeLessonHandler;
        private readonly GetLessonProgressHandler _getLessonProgressHandler;

        private readonly CreateQuizHandler _createQuizHandler;
        private readonly GetQuizHandler _getQuizHandler;

        private readonly UpdateQuizHandler _updateQuizHandler;

        private readonly PublishQuizHandler _publishQuizHandler;


        private readonly UnpublishQuizHandler _unpublishQuizHandler;

        private readonly DeleteQuizHandler _deleteQuizHandler;







        public LessonsController(
            CreateLessonHandler createLessonHandler,
            GetLessonsHandler getLessonsHandler,
            GetLessonByIdHandler getLessonByIdHandler,
            UpdateLessonHandler updateLessonHandler,
            DeleteLessonHandler deleteLessonHandler,
            PublishLessonHandler publishLessonHandler,
            UnpublishLessonHandler unpublishLessonHandler,
            StartLessonHandler startLessonHandler,
            UpdateLessonProgressHandler updateLessonProgressHandler,
            CompleteLessonHandler completeLessonHandler,
            GetLessonProgressHandler getLessonProgressHandler, CreateQuizHandler createQuizHandler, GetQuizHandler getQuizHandler, UpdateQuizHandler updateQuizHandler, PublishQuizHandler publishQuizHandler, UnpublishQuizHandler unpublishQuizHandler, DeleteQuizHandler deleteQuizHandler)
        {
            _createLessonHandler = createLessonHandler;
            _getLessonsHandler = getLessonsHandler;
            _getLessonByIdHandler = getLessonByIdHandler;
            _updateLessonHandler = updateLessonHandler;
            _deleteLessonHandler = deleteLessonHandler;
            _publishLessonHandler = publishLessonHandler;
            _unpublishLessonHandler = unpublishLessonHandler;
            _startLessonHandler = startLessonHandler;
            _updateLessonProgressHandler = updateLessonProgressHandler;
            _completeLessonHandler = completeLessonHandler;
            _getLessonProgressHandler = getLessonProgressHandler;
            _createQuizHandler = createQuizHandler;
            _getQuizHandler = getQuizHandler;
            _updateQuizHandler = updateQuizHandler;
            _publishQuizHandler = publishQuizHandler;
            _unpublishQuizHandler = unpublishQuizHandler;
            _deleteQuizHandler = deleteQuizHandler;

        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPost("/api/Sections/{sectionId:guid}/lessons")]
        public async Task<IActionResult> Create(
            Guid sectionId,
            [FromBody] CreateLessonRequest request,
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
                var result =
                    await _createLessonHandler.HandleAsync(
                        sectionId,
                        request,
                        currentUserId,
                        isAdmin,
                        cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Lesson created successfully.",
                        data = result
                    });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpGet("/api/Sections/{sectionId:guid}/lessons")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LessonResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid learner user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBySection(
            Guid sectionId,
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
                var result =
                    await _getLessonsHandler.HandleAsync(
                        sectionId,
                        userId,
                        canViewUnpublished,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lessons retrieved successfully.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpGet("/api/Lessons/{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<LessonResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid learner user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(
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
                var result =
                    await _getLessonByIdHandler.HandleAsync(
                        id,
                        userId,
                        canViewUnpublished,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lesson retrieved successfully.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPut("/api/Lessons/{id:guid}")]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateLessonRequest request,
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
                var result =
                    await _updateLessonHandler.HandleAsync(
                        id,
                        request,
                        currentUserId,
                        isAdmin,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lesson updated successfully.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpDelete("/api/Lessons/{id:guid}")]
        public async Task<IActionResult> Delete(
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
                await _deleteLessonHandler.HandleAsync(
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lesson deleted successfully."
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPatch("/api/Lessons/{id:guid}/publish")]
        public async Task<IActionResult> Publish(
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
                await _publishLessonHandler.HandleAsync(
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lesson published successfully."
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPatch("/api/Lessons/{id:guid}/unpublish")]
        public async Task<IActionResult> Unpublish(
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
                await _unpublishLessonHandler.HandleAsync(
                    id,
                    currentUserId,
                    isAdmin,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Lesson unpublished successfully."
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }


        [Authorize(Roles = "Student")]
        [HttpPost("/api/Lessons/{id:guid}/start")]
        public async Task<IActionResult> Start(
    Guid id,
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

            var result = await _startLessonHandler.HandleAsync(
                id,
                email,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson started successfully.",
                data = result
            });
        }

        [Authorize(Roles = "Student")]
        [HttpPatch("/api/Lessons/{id:guid}/progress")]
        public async Task<IActionResult> UpdateProgress(
    Guid id,
    [FromBody] UpdateLessonProgressRequest request,
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

            var result = await _updateLessonProgressHandler.HandleAsync(
                id,
                request,
                email,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson progress updated successfully.",
                data = result
            });
        }


        [Authorize(Roles = "Student")]
        [HttpPatch("/api/Lessons/{id:guid}/complete")]
        public async Task<IActionResult> Complete(
    Guid id,
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

            var result = await _completeLessonHandler.HandleAsync(
                id,
                email,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson completed successfully.",
                data = result
            });
        }

        [Authorize(Roles = "Student")]
        [HttpGet("/api/Lessons/{id:guid}/progress")]
        public async Task<IActionResult> GetProgress(
    Guid id,
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

            var result = await _getLessonProgressHandler.HandleAsync(
                id,
                email,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson progress retrieved successfully.",
                data = result
            });
        }




        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPost("{lessonId:guid}/quiz")]
        [ProducesResponseType(typeof(ApiResponse<QuizResponse>), StatusCodes.Status201Created, Description = "Success envelope contains success, message and data; errors is omitted.")]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateQuiz(
    Guid lessonId,
    CreateQuizRequest request,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var result = await _createQuizHandler.HandleAsync(
                    lessonId,
                    request,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Quiz created successfully.",
                        data = result
                    });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }



        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        [HttpGet("{lessonId:guid}/quiz")]
        [ProducesResponseType(typeof(ApiResponse<QuizResponse>), StatusCodes.Status200OK, Description = "Success envelope contains success, message and data; errors is omitted.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Read access denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            var canViewUnpublished =
                User.IsInRole("Instructor") ||
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var result = await _getQuizHandler.HandleAsync(
                    lessonId,
                    userId,
                    canViewUnpublished,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Quiz retrieved successfully.",
                    data = result
                });
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(ApiResponse<object>.Fail(exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }



        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPut("{lessonId:guid}/quiz")]
        [ProducesResponseType(typeof(ApiResponse<QuizResponse>), StatusCodes.Status200OK, Description = "Success envelope contains success, message and data; errors is omitted.")]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateQuiz(
    Guid lessonId,
    UpdateQuizRequest request,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var result = await _updateQuizHandler.HandleAsync(
                    lessonId,
                    request,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Quiz updated successfully.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPatch("{lessonId:guid}/quiz/publish")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK, Description = "Success envelope contains only success and message; data and errors are omitted.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PublishQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _publishQuizHandler.HandleAsync(
                    lessonId,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Quiz published successfully."
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }



        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPatch("{lessonId:guid}/quiz/unpublish")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK, Description = "Success envelope contains only success and message; data and errors are omitted.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UnpublishQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _unpublishQuizHandler.HandleAsync(
                    lessonId,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Quiz unpublished successfully."
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }

        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpDelete("{lessonId:guid}/quiz")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK, Description = "Success envelope contains only success and message; data and errors are omitted.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _deleteQuizHandler.HandleAsync(
                    lessonId,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Quiz deleted successfully."
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }







    }
}
