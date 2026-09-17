using LMS.Application.Interfaces.Identity;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;
using LMS.Application.DTOs.Identity;

namespace LMS.Infrastructure.Identity
{
    public sealed class IdentityService : IIdentityService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;

        public IdentityService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IdentityResultDto> CreateUserAsync(
            string firstName,
            string lastName,
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            };

            var result = await _userManager.CreateAsync(
                user,
                password);

            if (!result.Succeeded)
            {
                return new IdentityResultDto
                {
                    Succeeded = false,
                    Errors = result.Errors
                        .Select(x => x.Description)
                        .ToArray()
                };
            }

            var roleExists =
                await _roleManager.RoleExistsAsync("Student");

            if (!roleExists)
            {
                return new IdentityResultDto
                {
                    Succeeded = false,
                    UserId = user.Id,
                    Errors =
                    [
                        "Student role does not exist."
                    ]
                };
            }

            var roleResult =
                await _userManager.AddToRoleAsync(
                    user,
                    "Student");

            if (!roleResult.Succeeded)
            {
                return new IdentityResultDto
                {
                    Succeeded = false,
                    UserId = user.Id,
                    Errors = roleResult.Errors
                        .Select(x => x.Description)
                        .ToArray()
                };
            }

            return new IdentityResultDto
            {
                Succeeded = true,
                UserId = user.Id
            };
        }

        public async Task<bool> CheckPasswordAsync(
            string email,
            string password)
        {
            var user = await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                return false;
            }

            return await _userManager.CheckPasswordAsync(
                user,
                password);
        }

        public async Task<Guid?> GetUserIdAsync(
            string email)
        {
            var user = await _userManager.FindByEmailAsync(email);

            return user?.Id;
        }

        public async Task<IdentityUserDto?> GetUserAsync(
    string email)
        {
            var user =
                await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                return null;
            }

            return new IdentityUserDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? string.Empty
            };
        }

        public async Task<IReadOnlyCollection<string>> GetRolesAsync(
            Guid userId)
        {
            var user =
                await _userManager.FindByIdAsync(
                    userId.ToString());

            if (user is null)
            {
                return [];
            }

            var roles =
                await _userManager.GetRolesAsync(user);

            return roles.ToArray();
        }


        public async Task<IdentityUserDto?> GetUserByIdAsync(
    Guid userId)
        {
            var user =
                await _userManager.FindByIdAsync(
                    userId.ToString());

            if (user is null)
            {
                return null;
            }

            return new IdentityUserDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? string.Empty
            };
        }



        public async Task<bool> UserExistsAsync(Guid userId)
        {
            var user = await _userManager.FindByIdAsync(
                userId.ToString());

            return user is not null;
        }

        public async Task<bool> IsInRoleAsync(
            Guid userId,
            string role)
        {
            var user = await _userManager.FindByIdAsync(
                userId.ToString());

            if (user is null)
            {
                return false;
            }

            return await _userManager.IsInRoleAsync(
                user,
                role);
        }
    }
}
