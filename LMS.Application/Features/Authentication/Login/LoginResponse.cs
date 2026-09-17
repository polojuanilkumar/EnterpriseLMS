using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Authentication.Login
{
    public sealed class LoginResponse
    {
        public string AccessToken { get; init; } = string.Empty;

        public DateTime ExpiresAt { get; init; }

        public Guid UserId { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public IReadOnlyCollection<string> Roles { get; init; }
            = [];
    }
}
