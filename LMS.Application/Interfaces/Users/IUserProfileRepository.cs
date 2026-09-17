using LMS.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Application.Interfaces.Users
{
    public interface IUserProfileRepository
    {
        Task<UserProfile?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            UserProfile profile,
            CancellationToken cancellationToken = default);

        Task UpdateAsync(
            UserProfile profile,
            CancellationToken cancellationToken = default);
    }
}
