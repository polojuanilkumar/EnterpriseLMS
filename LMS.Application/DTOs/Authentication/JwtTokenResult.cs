using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.DTOs.Authentication
{
    public sealed class JwtTokenResult
    {
        public string AccessToken { get; init; } = string.Empty;

        public DateTime ExpiresAt { get; init; }
    }
}
