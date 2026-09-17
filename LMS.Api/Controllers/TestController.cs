using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public sealed class TestController : ControllerBase
    {
        [HttpGet("public")]
        public IActionResult Public()
        {
            return Ok(new
            {
                Success = true,
                Message = "This is a public endpoint."
            });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            var email =
                User.FindFirstValue(
                    ClaimTypes.Email);

            var roles =
                User.FindAll(
                        ClaimTypes.Role)
                    .Select(x => x.Value)
                    .ToArray();

            return Ok(new
            {
                Success = true,
                Message = "JWT authentication is working.",
                Data = new
                {
                    UserId = userId,
                    Email = email,
                    Roles = roles
                }
            });
        }
        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public IActionResult AdminOnly()
        {
            return Ok(new
            {
                Success = true,
                Message = "You are an Admin."
            });
        }
    }
}
