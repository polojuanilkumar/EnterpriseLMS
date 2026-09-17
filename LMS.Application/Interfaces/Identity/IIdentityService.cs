using LMS.Application.DTOs.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Identity
{
    public interface IIdentityService
    {
        Task<IdentityResultDto> CreateUserAsync(
           string firstName,
           string lastName,
           string email,
           string password,
           CancellationToken cancellationToken = default);

        Task<bool> CheckPasswordAsync(
            string email,
            string password);

        Task<Guid?> GetUserIdAsync(
            string email);

        Task<IdentityUserDto?> GetUserAsync(
            string email);

        Task<IReadOnlyCollection<string>> GetRolesAsync(
            Guid userId);

        Task<IdentityUserDto?> GetUserByIdAsync(
    Guid userId);

        Task<bool> UserExistsAsync(Guid userId);

        Task<bool> IsInRoleAsync(
            Guid userId,
            string role);
    }

    public sealed class IdentityResultDto
    {
        public bool Succeeded { get; init; }

        public Guid? UserId { get; init; }

        public string[] Errors { get; init; } = [];
    }
}
