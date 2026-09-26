using LMS.Application.Features.QuizQuestions.Common;
using System.Security.Claims;
using LMS.Application.Common.Models;
﻿using LMS.Application.Features.QuizOptions.CreateOption;
using LMS.Application.Features.QuizOptions.DeleteOption;
using LMS.Application.Features.QuizOptions.GetOptions;
using LMS.Application.Features.QuizOptions.GetStudentOptions;
using LMS.Application.Features.QuizOptions.UpdateOption;
using LMS.Application.Features.QuizQuestions.CreateQuestion;
using LMS.Application.Features.QuizQuestions.DeleteQuestion;
using LMS.Application.Features.QuizQuestions.GetQuestionById;
using LMS.Application.Features.QuizQuestions.GetQuestions;
using LMS.Application.Features.QuizQuestions.UpdateQuestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Route("api/Quizzes/{quizId:guid}/questions")]
    [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
    public sealed class QuizQuestionsController : ControllerBase
    {
        private readonly CreateQuestionHandler _createQuestionHandler;

        private readonly GetQuestionsHandler _getQuestionsHandler;

        private readonly GetQuestionByIdHandler _getQuestionByIdHandler;

        private readonly UpdateQuestionHandler _updateQuestionHandler;

        private readonly DeleteQuestionHandler _deleteQuestionHandler;

        private readonly CreateOptionHandler _createOptionHandler;

        private readonly GetOptionsHandler _getOptionsHandler;

        private readonly GetStudentOptionsHandler _getStudentOptionsHandler;

        private readonly UpdateOptionHandler _updateOptionHandler;

        private readonly DeleteOptionHandler _deleteOptionHandler;

        public QuizQuestionsController(
            CreateQuestionHandler createQuestionHandler, GetQuestionsHandler getQuestionsHandler, GetQuestionByIdHandler getQuestionByIdHandler, UpdateQuestionHandler updateQuestionHandler, DeleteQuestionHandler deleteQuestionHandler, CreateOptionHandler createOptionHandler, GetOptionsHandler getOptionsHandler, UpdateOptionHandler updateOptionHandler, DeleteOptionHandler deleteOptionHandler, GetStudentOptionsHandler getStudentOptionsHandler)
        {
            _createQuestionHandler = createQuestionHandler;
            _getQuestionsHandler = getQuestionsHandler;
            _getQuestionByIdHandler = getQuestionByIdHandler;
            _updateQuestionHandler = updateQuestionHandler;
            _deleteQuestionHandler = deleteQuestionHandler;
            _createOptionHandler = createOptionHandler;
            _getOptionsHandler = getOptionsHandler;
            _getStudentOptionsHandler = getStudentOptionsHandler;
            _updateOptionHandler = updateOptionHandler;
            _deleteOptionHandler = deleteOptionHandler;
        }

        [HttpPost]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<Dictionary<string, Guid>>), StatusCodes.Status201Created, Description = "Success envelope with data containing the questionId GUID.")]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateQuestion(
            Guid quizId,
            [FromBody] CreateQuestionRequest request,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var questionId =
                    await _createQuestionHandler.HandleAsync(
                        quizId,
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
                        message = "Question created successfully.",
                        data = new
                        {
                            questionId
                        },
                        errors = (object?)null
                    });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [HttpGet]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<QuizQuestionResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Read access denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetQuestions(
    Guid quizId,
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
                var questions = await _getQuestionsHandler.HandleAsync(
                    quizId,
                    userId,
                    canViewUnpublished,
                    cancellationToken);

                return Ok(
                    new
                    {
                        success = true,
                        message = "Questions retrieved successfully.",
                        data = questions,
                        errors = (object?)null
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




        [HttpGet("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        [ProducesResponseType(typeof(ApiResponse<QuizQuestionResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Read access denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetQuestionById(
    Guid quizId,
    Guid questionId,
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
                var question = await _getQuestionByIdHandler.HandleAsync(
                    questionId,
                    userId,
                    canViewUnpublished,
                    cancellationToken);

                if (question.QuizId != quizId)
                {
                    return NotFound(
                        new
                        {
                            success = false,
                            message = "Question not found.",
                            data = (object?)null,
                            errors = (object?)null
                        });
                }

                return Ok(
                    new
                    {
                        success = true,
                        message = "Question retrieved successfully.",
                        data = question,
                        errors = (object?)null
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





        [HttpPut("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateQuestion(
    Guid quizId,
    Guid questionId,
    [FromBody] UpdateQuestionRequest request,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _updateQuestionHandler.HandleAsync(
                    quizId,
                    questionId,
                    request,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(
                    new
                    {
                        success = true,
                        message = "Question updated successfully.",
                        data = (object?)null,
                        errors = (object?)null
                    });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [HttpDelete("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteQuestion(
    Guid quizId,
    Guid questionId,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _deleteQuestionHandler.HandleAsync(
                    quizId,
                    questionId,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(
                    new
                    {
                        success = true,
                        message = "Question deleted successfully.",
                        data = (object?)null,
                        errors = (object?)null
                    });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [HttpPost("{questionId:guid}/options")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<Dictionary<string, Guid>>), StatusCodes.Status201Created, Description = "Success envelope with data containing the optionId GUID.")]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateOption(
    Guid quizId,
    Guid questionId,
    [FromBody] CreateOptionRequest request,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                var optionId = await _createOptionHandler.HandleAsync(
                    quizId,
                    questionId,
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
                        message = "Option created successfully.",
                        data = new { optionId },
                        errors = (object?)null
                    });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [HttpGet("{questionId:guid}/options")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<QuizOptionResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Description = "JWT authentication challenge; no response body.")]
        [ProducesResponseType(StatusCodes.Status403Forbidden, Description = "Role authorization failure; no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOptions(
    Guid quizId,
    Guid questionId,
    CancellationToken cancellationToken)
        {
            var options = await _getOptionsHandler.HandleAsync(
                quizId,
                questionId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Options retrieved successfully.",
                data = options,
                errors = (object?)null
            });
        }


        [HttpGet("{questionId:guid}/options/student")]
        [Authorize(Roles = "Student")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StudentQuizOptionResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Read access denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStudentOptions(
            Guid quizId,
            Guid questionId,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var options = await _getStudentOptionsHandler.HandleAsync(
                    quizId,
                    questionId,
                    userId,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Options retrieved successfully.",
                    data = options,
                    errors = (object?)null
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


        [HttpPut("{questionId:guid}/options/{optionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateOption(
    Guid quizId,
    Guid questionId,
    Guid optionId,
    [FromBody] UpdateOptionRequest request,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _updateOptionHandler.HandleAsync(
                    quizId,
                    questionId,
                    optionId,
                    request,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Option updated successfully.",
                    data = (object?)null,
                    errors = (object?)null
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }


        [HttpDelete("{questionId:guid}/options/{optionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Ownership denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteOption(
    Guid quizId,
    Guid questionId,
    Guid optionId,
    CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                || currentUserId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            try
            {
                await _deleteOptionHandler.HandleAsync(
                    quizId,
                    questionId,
                    optionId,
                    currentUserId,
                    isAdmin,
                    User.IsInRole("Instructor"),
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Option deleted successfully.",
                    data = (object?)null,
                    errors = (object?)null
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
        }





    }
}
