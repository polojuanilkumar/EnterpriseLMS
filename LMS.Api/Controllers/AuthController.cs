using LMS.Application.Features.Authentication.Login;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public sealed class AuthController : ControllerBase
    {
        private readonly LoginHandler _loginHandler;

        public AuthController(LoginHandler loginHandler)
        {
            _loginHandler = loginHandler;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request)
        {
            var result =
                await _loginHandler.HandleAsync(request);

            if (result is null)
            {
                return Unauthorized(new
                {
                    Success = false,
                    Message = "Invalid email or password.",
                    Data = (object?)null
                });
            }

            return Ok(new
            {
                Success = true,
                Message = "Login successful.",
                Data = result,
                Errors = (object?)null
            });
        }
    }
}
