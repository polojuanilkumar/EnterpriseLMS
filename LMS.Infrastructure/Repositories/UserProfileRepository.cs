using LMS.Application.Interfaces.Users;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Repositories
{
    public sealed class UserProfileRepository : IUserProfileRepository
    {
        private readonly LMSDbContext _context;

        public UserProfileRepository(LMSDbContext context)
        {
            _context = context;
        }

        public async Task<UserProfile?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.UserProfiles
                .FirstOrDefaultAsync(
                    x => x.UserId == userId,
                    cancellationToken);
        }

        public async Task AddAsync(
              UserProfile profile,
              CancellationToken cancellationToken = default)
        {
            await _context.UserProfiles.AddAsync(
                profile,
                cancellationToken);

            //await _context.SaveChangesAsync(
            //    cancellationToken);
        }

        public Task UpdateAsync(
            UserProfile profile,
            CancellationToken cancellationToken = default)
        {
            _context.UserProfiles.Update(profile);

            //await _context.SaveChangesAsync(
            //    cancellationToken);

            return Task.CompletedTask;
        }
    }
}
