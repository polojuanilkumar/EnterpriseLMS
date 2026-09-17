using LMS.Application.Interfaces.Persistence;
using LMS.Application.Interfaces.Users;
using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Features.Users.Profile
{
    public sealed class UpdateMyProfileHandler
    {
        private readonly IUserProfileRepository _profileRepository;

        private readonly IUnitOfWork _unitOfWork;


        public UpdateMyProfileHandler(
            IUserProfileRepository profileRepository,
            IUnitOfWork unitOfWork)
        {
            _profileRepository = profileRepository;
            _unitOfWork = unitOfWork;
        }


        public async Task HandleAsync(
            Guid userId,
            UpdateUserProfileRequest request,
            CancellationToken cancellationToken = default)
        {
            var profile =
                await _profileRepository.GetByUserIdAsync(
                    userId,
                    cancellationToken);

            if (profile is null)
            {
                profile = new UserProfile(userId);

                profile.Update(
                    request.PhoneNumber,
                    request.EmployeeCode,
                    request.Department,
                    request.Designation,
                    request.ProfileImageUrl);

                await _profileRepository.AddAsync(
                    profile,
                    cancellationToken);
            }
            else
            {
                profile.Update(
                    request.PhoneNumber,
                    request.EmployeeCode,
                    request.Department,
                    request.Designation,
                    request.ProfileImageUrl);

                await _profileRepository.UpdateAsync(
                    profile,
                    cancellationToken);
            }
            await _unitOfWork.SaveChangesAsync(
    cancellationToken);
        }
    }
}
