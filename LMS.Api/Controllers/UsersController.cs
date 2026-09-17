using LMS.Application.Common.Models;
using LMS.Application.DTOs.Users;
using LMS.Application.Features.Users.Commands.RegisterUser;
using LMS.Application.Features.Users.Profile;
using LMS.Application.Validators.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly RegisterUserHandler _registerUserHandler;
        private readonly GetMyProfileHandler _getMyProfileHandler;

        private readonly UpdateMyProfileHandler _updateMyProfileHandler;

        public UsersController(
            RegisterUserHandler registerUserHandler,
            GetMyProfileHandler getMyProfileHandler,
            UpdateMyProfileHandler updateMyProfileHandler)
        {
            _registerUserHandler = registerUserHandler;
            _getMyProfileHandler = getMyProfileHandler;
            _updateMyProfileHandler = updateMyProfileHandler;
        }
        // ============================================
        // POST: api/Users/register
        // ============================================
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register(
            [FromBody] RegisterUserRequest request,
            CancellationToken cancellationToken)
        {
            var validator = new RegisterUserValidator();

            var validationResult =
                await validator.ValidateAsync(
                    request,
                    cancellationToken);

            //if (!validationResult.IsValid)
            //{
            //    return BadRequest(
            //        validationResult.Errors.Select(x => new
            //        {
            //            Field = x.PropertyName,
            //            Message = x.ErrorMessage
            //        }));
            //}

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .Select(x => new
                    {
                        Field = x.PropertyName,
                        Message = x.ErrorMessage
                    })
                    .ToList();

                return BadRequest(
                    ApiResponse<object>.Fail(
                        "Validation failed.",
                        errors));
            }

            var result =
                await _registerUserHandler.HandleAsync(
                    request,
                    cancellationToken);

            return Ok(
     ApiResponse<RegisterUserResponse>.Ok(
         result,
         "User registered successfully."));
        }




        // ============================================
        // GET: api/Users/me
        // ============================================
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile(
            CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Unauthorized();
            }

            var result =
                await _getMyProfileHandler.HandleAsync(
                    userId,
                    cancellationToken);

            if (result is null)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = "User not found."
                });
            }

            return Ok(new
            {
                Success = true,
                Message = "User profile retrieved successfully.",
                Data = result
            });
        }
        // ============================================
        // PUT: api/Users/me
        // ============================================
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile(
    [FromBody] UpdateUserProfileRequest request,
    CancellationToken cancellationToken)
        {
            var userIdValue =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Unauthorized();
            }

            await _updateMyProfileHandler.HandleAsync(
                userId,
                request,
                cancellationToken);

            return Ok(new
            {
                Success = true,
                Message = "User profile updated successfully."
            });
        }






    }
}
