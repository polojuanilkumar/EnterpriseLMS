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
        private readonly LMS.Application.Features.Organizations.OrganizationContext _organization;
        private readonly LMS.Application.Interfaces.Organizations.IOrganizationRepository _organizations;

        public IdentityService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            LMS.Application.Features.Organizations.OrganizationContext organization,
            LMS.Application.Interfaces.Organizations.IOrganizationRepository organizations)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _organization = organization;
            _organizations = organizations;
        }

        public async Task<IdentityResultDto> CreateUserAsync(
            string firstName,
            string lastName,
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(new IdentityResultDto { Succeeded = false, Errors = ["Registration requires an organization invitation."] });
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
            if (role is "Admin" or "Instructor" or "Student")
            {
                if (!_organization.OrganizationId.HasValue) return false;
                var membership = await _organizations.GetMembershipAsync(_organization.OrganizationId.Value, userId);
                return membership?.Role.ToString() == role;
            }
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
