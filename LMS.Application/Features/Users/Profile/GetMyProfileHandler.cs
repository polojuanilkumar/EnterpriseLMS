using LMS.Application.Interfaces.Identity;
using LMS.Application.Interfaces.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Profile
{
    public sealed class GetMyProfileHandler
    {
        private readonly IIdentityService _identityService;
        private readonly IUserProfileRepository _profileRepository;

        public GetMyProfileHandler(
            IIdentityService identityService,
            IUserProfileRepository profileRepository)
        {
            _identityService = identityService;
            _profileRepository = profileRepository;
        }

        public async Task<UserProfileResponse?> HandleAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user =
                await _identityService.GetUserByIdAsync(
                    userId);

            if (user is null)
            {
                return null;
            }

            var profile =
                await _profileRepository.GetByUserIdAsync(
                    userId,
                    cancellationToken);

            return new UserProfileResponse
            {
                UserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,

                PhoneNumber = profile?.PhoneNumber,
                EmployeeCode = profile?.EmployeeCode,
                Department = profile?.Department,
                Designation = profile?.Designation,
                ProfileImageUrl = profile?.ProfileImageUrl,
                IsActive = profile?.IsActive ?? true
            };
        }
    }
}
