using LMS.Application.Features.Categories.ActivateCategory;
using LMS.Application.Features.Categories.CreateCategory;
using LMS.Application.Features.Categories.DeactivateCategory;
using LMS.Application.Features.Categories.GetCategories;
using LMS.Application.Features.Categories.GetCategoryById;
using LMS.Application.Features.Categories.UpdateCategory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public sealed class CategoriesController : ControllerBase
    {
        private readonly CreateCategoryHandler _createCategoryHandler;
        private readonly GetCategoriesHandler _getCategoriesHandler;
        private readonly GetCategoryByIdHandler _getCategoryByIdHandler;
        private readonly UpdateCategoryHandler _updateCategoryHandler;

        private readonly ActivateCategoryHandler _activateCategoryHandler;
        private readonly DeactivateCategoryHandler _deactivateCategoryHandler;

        public CategoriesController(
            CreateCategoryHandler createCategoryHandler, GetCategoriesHandler getCategoriesHandler, GetCategoryByIdHandler getCategoryByIdHandler, UpdateCategoryHandler updateCategoryHandler, ActivateCategoryHandler activateCategoryHandler,
    DeactivateCategoryHandler deactivateCategoryHandler)
        {
            _createCategoryHandler = createCategoryHandler;
            _getCategoriesHandler = getCategoriesHandler;
            _getCategoryByIdHandler = getCategoryByIdHandler;
            _updateCategoryHandler = updateCategoryHandler;
            _activateCategoryHandler = activateCategoryHandler;
            _deactivateCategoryHandler = deactivateCategoryHandler;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Create(
            [FromBody] CreateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var category =
                    await _createCategoryHandler.HandleAsync(
                        request,
                        cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Category created successfully.",
                        data = category
                    });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }



        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll(
    CancellationToken cancellationToken)
        {
            var categories =
                await _getCategoriesHandler.HandleAsync(
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Categories retrieved successfully.",
                data = categories
            });
        }



        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(
    Guid id,
    CancellationToken cancellationToken)
        {
            try
            {
                var category =
                    await _getCategoryByIdHandler.HandleAsync(
                        id,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Category retrieved successfully.",
                    data = category
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Update(
    Guid id,
    [FromBody] UpdateCategoryRequest request,
    CancellationToken cancellationToken)
        {
            try
            {
                var category =
                    await _updateCategoryHandler.HandleAsync(
                        id,
                        request,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Category updated successfully.",
                    data = category
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPatch("{id:guid}/activate")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Activate(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                await _activateCategoryHandler.HandleAsync(
                    id,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Category activated successfully."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        [HttpPatch("{id:guid}/deactivate")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Deactivate(
            Guid id,
            CancellationToken cancellationToken)
        {
            try
            {
                await _deactivateCategoryHandler.HandleAsync(
                    id,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Category deactivated successfully."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


















    }
}
