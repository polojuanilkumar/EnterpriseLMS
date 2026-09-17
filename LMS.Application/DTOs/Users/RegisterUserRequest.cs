using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.DTOs.Users
{
    public sealed class RegisterUserRequest
    {
        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string Password { get; init; } = string.Empty;
    }
    public sealed class RegisterUserResponse
    {
        public Guid UserId { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;
    }
}
