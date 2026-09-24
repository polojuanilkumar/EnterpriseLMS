using LMS.Application.Features.QuizOptions.CreateOption;
using LMS.Application.Features.QuizOptions.DeleteOption;
using LMS.Application.Features.QuizOptions.GetOptions;
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

        private readonly UpdateOptionHandler _updateOptionHandler;

        private readonly DeleteOptionHandler _deleteOptionHandler;

        public QuizQuestionsController(
            CreateQuestionHandler createQuestionHandler, GetQuestionsHandler getQuestionsHandler, GetQuestionByIdHandler getQuestionByIdHandler, UpdateQuestionHandler updateQuestionHandler, DeleteQuestionHandler deleteQuestionHandler, CreateOptionHandler createOptionHandler, GetOptionsHandler getOptionsHandler, UpdateOptionHandler updateOptionHandler, DeleteOptionHandler deleteOptionHandler)
        {
            _createQuestionHandler = createQuestionHandler;
            _getQuestionsHandler = getQuestionsHandler;
            _getQuestionByIdHandler = getQuestionByIdHandler;
            _updateQuestionHandler = updateQuestionHandler;
            _deleteQuestionHandler = deleteQuestionHandler;
            _createOptionHandler = createOptionHandler;
            _getOptionsHandler = getOptionsHandler;
            _updateOptionHandler = updateOptionHandler;
            _deleteOptionHandler = deleteOptionHandler;
        }

        [HttpPost]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestion(
            Guid quizId,
            [FromBody] CreateQuestionRequest request,
            CancellationToken cancellationToken)
        {
            var questionId =
                await _createQuestionHandler.HandleAsync(
                    quizId,
                    request,
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


        [HttpGet]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        public async Task<IActionResult> GetQuestions(
    Guid quizId,
    CancellationToken cancellationToken)
        {
            var canViewUnpublished =
                User.IsInRole("Instructor") ||
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            var questions = await _getQuestionsHandler.HandleAsync(
                quizId,
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




        [HttpGet("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        public async Task<IActionResult> GetQuestionById(
    Guid quizId,
    Guid questionId,
    CancellationToken cancellationToken)
        {
            var canViewUnpublished =
                User.IsInRole("Instructor") ||
                User.IsInRole("Admin") ||
                User.IsInRole("SuperAdmin");

            var question = await _getQuestionByIdHandler.HandleAsync(
                questionId,
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





        [HttpPut("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestion(
    Guid quizId,
    Guid questionId,
    [FromBody] UpdateQuestionRequest request,
    CancellationToken cancellationToken)
        {
            await _updateQuestionHandler.HandleAsync(
                quizId,
                questionId,
                request,
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


        [HttpDelete("{questionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestion(
    Guid quizId,
    Guid questionId,
    CancellationToken cancellationToken)
        {
            await _deleteQuestionHandler.HandleAsync(
                quizId,
                questionId,
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


        [HttpPost("{questionId:guid}/options")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateOption(
    Guid quizId,
    Guid questionId,
    [FromBody] CreateOptionRequest request,
    CancellationToken cancellationToken)
        {
            var optionId = await _createOptionHandler.HandleAsync(
                quizId,
                questionId,
                request,
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


        [HttpGet("{questionId:guid}/options")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
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


        [HttpPut("{questionId:guid}/options/{optionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateOption(
    Guid quizId,
    Guid questionId,
    Guid optionId,
    [FromBody] UpdateOptionRequest request,
    CancellationToken cancellationToken)
        {
            await _updateOptionHandler.HandleAsync(
                quizId,
                questionId,
                optionId,
                request,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Option updated successfully.",
                data = (object?)null,
                errors = (object?)null
            });
        }


        [HttpDelete("{questionId:guid}/options/{optionId:guid}")]
        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteOption(
    Guid quizId,
    Guid questionId,
    Guid optionId,
    CancellationToken cancellationToken)
        {
            await _deleteOptionHandler.HandleAsync(
                quizId,
                questionId,
                optionId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Option deleted successfully.",
                data = (object?)null,
                errors = (object?)null
            });
        }





    }
}
