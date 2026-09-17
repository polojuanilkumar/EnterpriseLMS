using LMS.Application.Interfaces.Authentication;
using LMS.Application.Interfaces.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Authentication.Login
{
    public sealed class LoginHandler
    {
        private readonly IIdentityService _identityService;
        private readonly IJwtTokenService _jwtTokenService;

        public LoginHandler(
            IIdentityService identityService,
            IJwtTokenService jwtTokenService)
        {
            _identityService = identityService;
            _jwtTokenService = jwtTokenService;
        }

        public async Task<LoginResponse?> HandleAsync(
            LoginRequest request)
        {
            var email = request.Email
                .Trim()
                .ToLowerInvariant();

            var isValid =
                await _identityService.CheckPasswordAsync(
                    email,
                    request.Password);

            if (!isValid)
            {
                return null;
            }

            var user =
                await _identityService.GetUserAsync(email);

            if (user is null)
            {
                return null;
            }

            var roles =
                await _identityService.GetRolesAsync(user.Id);

            var tokenResult =
        await _jwtTokenService.GenerateTokenAsync(
            user.Id,
            user.Email,
            roles);

            return new LoginResponse
            {
                AccessToken = tokenResult.AccessToken,
                ExpiresAt = tokenResult.ExpiresAt,
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Roles = roles
            };
        }
    }
}
