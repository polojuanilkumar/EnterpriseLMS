using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Profile
{
    public sealed class UserProfileResponse
    {
        public Guid UserId { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? EmployeeCode { get; init; }

        public string? Department { get; init; }

        public string? Designation { get; init; }

        public string? ProfileImageUrl { get; init; }

        public bool IsActive { get; init; }
    }
}
