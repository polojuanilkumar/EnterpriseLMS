using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Profile
{
    public sealed class UpdateUserProfileRequest
    {
        public string? PhoneNumber { get; init; }

        public string? EmployeeCode { get; init; }

        public string? Department { get; init; }

        public string? Designation { get; init; }

        public string? ProfileImageUrl { get; init; }
    }
}
