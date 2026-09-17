using LMS.Application.DTOs.Users;
using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Commands.RegisterUser
{
    public sealed class RegisterUserHandler
    {
        private readonly IIdentityService _identityService;

        public RegisterUserHandler(
            IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<RegisterUserResponse> HandleAsync(
            RegisterUserRequest request,
            CancellationToken cancellationToken = default)
        {
            var email = request.Email
                .Trim()
                .ToLowerInvariant();

            var result =
                await _identityService.CreateUserAsync(
                    request.FirstName,
                    request.LastName,
                    email,
                    request.Password,
                    cancellationToken);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(
                        " ",
                        result.Errors));
            }

            return new RegisterUserResponse
            {
                UserId = result.UserId!.Value,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = email,
                Role = "Student"
            };
        }
    }
}
