using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Authentication.Login
{
    public sealed class LoginRequest
    {
        public string Email { get; init; } = string.Empty;

        public string Password { get; init; } = string.Empty;
    }
}
