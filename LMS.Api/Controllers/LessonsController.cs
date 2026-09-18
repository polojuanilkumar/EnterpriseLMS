using LMS.Application.Features.LessonProgress.CompleteLesson;
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
using LMS.Application.Features.Quizzes.GetQuiz;
using LMS.Application.Features.Quizzes.PublishQuiz;
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
            GetLessonProgressHandler getLessonProgressHandler, CreateQuizHandler createQuizHandler, GetQuizHandler getQuizHandler, UpdateQuizHandler updateQuizHandler, PublishQuizHandler publishQuizHandler)
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

        }

        [HttpPost("/api/Sections/{sectionId:guid}/lessons")]
        public async Task<IActionResult> Create(
            Guid sectionId,
            [FromBody] CreateLessonRequest request,
            CancellationToken cancellationToken)
        {
            var result =
                await _createLessonHandler.HandleAsync(
                    sectionId,
                    request,
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

        [HttpGet("/api/Sections/{sectionId:guid}/lessons")]
        public async Task<IActionResult> GetBySection(
            Guid sectionId,
            CancellationToken cancellationToken)
        {
            var result =
                await _getLessonsHandler.HandleAsync(
                    sectionId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lessons retrieved successfully.",
                data = result
            });
        }

        [HttpGet("/api/Lessons/{id:guid}")]
        public async Task<IActionResult> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result =
                await _getLessonByIdHandler.HandleAsync(
                    id,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson retrieved successfully.",
                data = result
            });
        }

        [HttpPut("/api/Lessons/{id:guid}")]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateLessonRequest request,
            CancellationToken cancellationToken)
        {
            var result =
                await _updateLessonHandler.HandleAsync(
                    id,
                    request,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson updated successfully.",
                data = result
            });
        }

        [HttpDelete("/api/Lessons/{id:guid}")]
        public async Task<IActionResult> Delete(
            Guid id,
            CancellationToken cancellationToken)
        {
            await _deleteLessonHandler.HandleAsync(
                id,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson deleted successfully."
            });
        }

        [HttpPatch("/api/Lessons/{id:guid}/publish")]
        public async Task<IActionResult> Publish(
            Guid id,
            CancellationToken cancellationToken)
        {
            await _publishLessonHandler.HandleAsync(
                id,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson published successfully."
            });
        }

        [HttpPatch("/api/Lessons/{id:guid}/unpublish")]
        public async Task<IActionResult> Unpublish(
            Guid id,
            CancellationToken cancellationToken)
        {
            await _unpublishLessonHandler.HandleAsync(
                id,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Lesson unpublished successfully."
            });
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
        public async Task<IActionResult> CreateQuiz(
    Guid lessonId,
    CreateQuizRequest request,
    CancellationToken cancellationToken)
        {
            var result = await _createQuizHandler.HandleAsync(
                lessonId,
                request,
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



        [Authorize(Roles = "Instructor,Admin,SuperAdmin,Student")]
        [HttpGet("{lessonId:guid}/quiz")]
        public async Task<IActionResult> GetQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            var result = await _getQuizHandler.HandleAsync(
                lessonId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Quiz retrieved successfully.",
                data = result
            });
        }



        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPut("{lessonId:guid}/quiz")]
        public async Task<IActionResult> UpdateQuiz(
    Guid lessonId,
    UpdateQuizRequest request,
    CancellationToken cancellationToken)
        {
            var result = await _updateQuizHandler.HandleAsync(
                lessonId,
                request,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Quiz updated successfully.",
                data = result
            });
        }


        [Authorize(Roles = "Instructor,Admin,SuperAdmin")]
        [HttpPatch("{lessonId:guid}/quiz/publish")]
        public async Task<IActionResult> PublishQuiz(
    Guid lessonId,
    CancellationToken cancellationToken)
        {
            await _publishQuizHandler.HandleAsync(
                lessonId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Quiz published successfully."
            });
        }







    }
}
