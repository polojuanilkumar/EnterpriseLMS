using LMS.Application.DTOs.Authentication;
using LMS.Application.Interfaces.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LMS.Infrastructure.Identity
{
    public sealed class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(
            IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<JwtTokenResult> GenerateTokenAsync(
            Guid userId,
            string email,
            IEnumerable<string> roles)
        {
            var key =
                _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException(
                    "JWT key is not configured.");

            var issuer =
                _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "JWT issuer is not configured.");

            var audience =
                _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "JWT audience is not configured.");

            var minutes =
                _configuration.GetValue<int>(
                    "Jwt:AccessTokenMinutes");

            var claims = new List<Claim>
    {
        new(
            JwtRegisteredClaimNames.Sub,
            userId.ToString()),

        new(
            JwtRegisteredClaimNames.Email,
            email),

        new(
            ClaimTypes.NameIdentifier,
            userId.ToString()),

        new(
            ClaimTypes.Email,
            email)
    };

            foreach (var role in roles)
            {
                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        role));
            }

            var securityKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key));

            var credentials =
                new SigningCredentials(
                    securityKey,
                    SecurityAlgorithms.HmacSha256);

            var expiresAt =
                DateTime.UtcNow.AddMinutes(minutes);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            var accessToken =
                new JwtSecurityTokenHandler()
                    .WriteToken(token);

            return Task.FromResult(
                new JwtTokenResult
                {
                    AccessToken = accessToken,
                    ExpiresAt = expiresAt
                });
        }
    }
}
