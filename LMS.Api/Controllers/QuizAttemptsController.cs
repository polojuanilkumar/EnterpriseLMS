using System.Security.Claims;
using LMS.Application.Common.Models;
using LMS.Application.Features.QuizAttempts.GetQuizAttemptAnswers;
using LMS.Application.Features.QuizAttempts.GetQuizAttemptResult;
using LMS.Application.Features.QuizAttempts.GetQuizAttempts;
using LMS.Application.Features.QuizAttempts.StartQuizAttempt;
using LMS.Application.Features.QuizAttempts.SubmitQuizAttempt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Route("api/Quizzes/{quizId:guid}/attempts")]
    [Authorize(Roles = "Student")]
    public sealed class QuizAttemptsController : ControllerBase
    {
        private readonly StartQuizAttemptHandler _startHandler;
        private readonly SubmitQuizAttemptHandler _submitHandler;
        private readonly GetQuizAttemptResultHandler _resultHandler;
        private readonly GetQuizAttemptsHandler _attemptsHandler;
        private readonly GetQuizAttemptAnswersHandler _answersHandler;

        public QuizAttemptsController(StartQuizAttemptHandler startHandler, SubmitQuizAttemptHandler submitHandler,
            GetQuizAttemptResultHandler resultHandler, GetQuizAttemptsHandler attemptsHandler,
            GetQuizAttemptAnswersHandler answersHandler)
        {
            _startHandler = startHandler;
            _submitHandler = submitHandler;
            _resultHandler = resultHandler;
            _attemptsHandler = attemptsHandler;
            _answersHandler = answersHandler;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<GetQuizAttemptsResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(StatusCodes.Status403Forbidden, Description = "Role authorization failure; no response body.")]
        public async Task<IActionResult> GetAttempts(Guid quizId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            var result = await _attemptsHandler.HandleAsync(quizId, userId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<GetQuizAttemptsResponse>>.Ok(result,
                "Quiz attempts retrieved successfully."));
        }

        [HttpGet("{attemptId:guid}/result")]
        [ProducesResponseType(typeof(ApiResponse<GetQuizAttemptResultResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(StatusCodes.Status403Forbidden, Description = "Role authorization failure; no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> GetResult(Guid quizId, Guid attemptId,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var result = await _resultHandler.HandleAsync(quizId, attemptId, userId, cancellationToken);
                return Ok(ApiResponse<GetQuizAttemptResultResponse>.Ok(result,
                    "Quiz attempt result retrieved successfully."));
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(ApiResponse<object>.Fail(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpGet("{attemptId:guid}/answers")]
        [ProducesResponseType(typeof(ApiResponse<GetQuizAttemptAnswersResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(StatusCodes.Status403Forbidden, Description = "Role authorization failure; no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> GetAnswers(Guid quizId, Guid attemptId,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var result = await _answersHandler.HandleAsync(quizId, attemptId, userId, cancellationToken);
                return Ok(ApiResponse<GetQuizAttemptAnswersResponse>.Ok(result,
                    "Quiz attempt answers retrieved successfully."));
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(ApiResponse<object>.Fail(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpPost("{attemptId:guid}/submit")]
        [ProducesResponseType(typeof(ApiResponse<SubmitQuizAttemptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest, Description = "ApiResponse<object> for invalid argument values; ValidationProblemDetails for automatic model binding or validation failures.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(StatusCodes.Status403Forbidden, Description = "Role authorization failure; no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Submit(Guid quizId, Guid attemptId,
            [FromBody] SubmitQuizAttemptRequest request, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var result = await _submitHandler.HandleAsync(quizId, attemptId, userId, request, cancellationToken);
                return Ok(ApiResponse<SubmitQuizAttemptResponse>.Ok(result, "Quiz attempt submitted successfully."));
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(ApiResponse<object>.Fail(exception.Message));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(ApiResponse<object>.Fail(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(ApiResponse<object>.Fail(exception.Message));
            }
        }

        [HttpPost("start")]
        [ProducesResponseType(typeof(ApiResponse<StartQuizAttemptResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized, Description = "Missing or invalid user ID returns ApiResponse<object>; a JWT challenge has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden, Description = "Read access denied returns ApiResponse<object>; role authorization failure has no response body.")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Start(Guid quizId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || userId == Guid.Empty)
                return Unauthorized(ApiResponse<object>.Fail("Authenticated user ID was not found."));

            try
            {
                var result = await _startHandler.HandleAsync(quizId, userId, cancellationToken);
                return StatusCode(StatusCodes.Status201Created,
                    ApiResponse<StartQuizAttemptResponse>.Ok(result, "Quiz attempt started successfully."));
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(ApiResponse<object>.Fail(exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(ApiResponse<object>.Fail(exception.Message));
            }
        }
    }
}
