using LMS.Application.Features.CourseSections;
using LMS.Application.Features.CourseSections.CreateCourseSection;
using LMS.Application.Features.CourseSections.DeleteCourseSection;
using LMS.Application.Features.CourseSections.GetCourseSectionById;
using LMS.Application.Features.CourseSections.GetCourseSections;
using LMS.Application.Features.CourseSections.UpdateCourseSection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> Create(
            Guid courseId,
            CreateCourseSectionRequest request,
            CancellationToken cancellationToken)
        {
            var section =
                await _createHandler.HandleAsync(
                    courseId,
                    request,
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

        [HttpGet]
        public async Task<IActionResult> GetAll(
            Guid courseId,
            CancellationToken cancellationToken)
        {
            var sections =
                await _getAllHandler.HandleAsync(
                    courseId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Course sections retrieved successfully.",
                data = sections
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(
            Guid courseId,
            Guid id,
            CancellationToken cancellationToken)
        {
            var section =
                await _getByIdHandler.HandleAsync(
                    courseId,
                    id,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Course section retrieved successfully.",
                data = section
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(
            Guid courseId,
            Guid id,
            UpdateCourseSectionRequest request,
            CancellationToken cancellationToken)
        {
            var section =
                await _updateHandler.HandleAsync(
                    courseId,
                    id,
                    request,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Course section updated successfully.",
                data = section
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(
            Guid courseId,
            Guid id,
            CancellationToken cancellationToken)
        {
            await _deleteHandler.HandleAsync(
                courseId,
                id,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Course section deleted successfully."
            });
        }
    }
}
