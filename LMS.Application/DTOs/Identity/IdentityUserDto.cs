using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.DTOs.Identity
{
    public sealed class IdentityUserDto
    {
        public Guid Id { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }
}
