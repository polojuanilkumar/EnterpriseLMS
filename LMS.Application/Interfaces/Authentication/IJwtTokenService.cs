using LMS.Application.DTOs.Authentication;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Authentication
{
    public interface IJwtTokenService
    {
        Task<JwtTokenResult> GenerateTokenAsync(
            Guid userId,
            string email,
            IEnumerable<string> roles);
    }
}
