using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using LMS.Application.Features.Organizations;

namespace LMS.Infrastructure.Repositories;

public sealed partial class OrganizationRepository(LMSDbContext db, OrganizationContext context) : IOrganizationRepository
{
    public Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken token = default)
        => (from assignment in db.UserRoles join role in db.Roles on assignment.RoleId equals role.Id
            where assignment.UserId == userId && role.NormalizedName == "SUPERADMIN" select assignment).AnyAsync(token);
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken token = default) => db.Users.AnyAsync(x => x.Id == userId, token);
    public Task<Organization?> GetAsync(Guid id, CancellationToken token = default)
        => db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
    public Task<OrganizationMembership?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken token = default)
        => db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId, token);
    public async Task<IReadOnlyList<Organization>> ListForUserAsync(Guid userId, CancellationToken token = default)
    {
        var platform = await IsSuperAdminAsync(userId, token);
        return await db.Organizations.AsNoTracking().Where(x => x.IsActive && (platform ||
            db.OrganizationMemberships.Any(m => m.OrganizationId == x.Id && m.UserId == userId)))
            .OrderBy(x => x.Code).ToListAsync(token);
    }
    public async Task AddAsync(Organization organization, OrganizationMembership initialAdmin, CancellationToken token = default)
    {
        if (initialAdmin.OrganizationId != organization.Id || initialAdmin.Role != OrganizationRole.Admin)
            throw new ArgumentException("Initial Admin must belong to the new organization.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        if (!await UserExistsAsync(initialAdmin.UserId, token) || await IsSuperAdminAsync(initialAdmin.UserId, token))
            throw new InvalidOperationException(LMS.Application.Features.Organizations.MembershipRules.Unavailable);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(initialAdmin);
        db.Stage(AuditAction.OrganizationCreated,organization.Id,organization.Id,AuditMetadata.Empty());
        db.Stage(AuditAction.MembershipCreated,initialAdmin.Id,organization.Id,AuditMetadata.Roles(null,initialAdmin.Role,initialAdmin.UserId));
        await SaveAsync(token); // One EF save/transaction for organization and initial admin.
        await transaction.CommitAsync(token);
    }
    public async Task SetMembershipAsync(Guid organizationId, Guid userId, OrganizationRole role, CancellationToken token = default)
    {
        await ChangeMembership(organizationId, userId, null, role, false, token);
    }
    private async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Errors.Cast<SqlError>().Any(e =>
            IsExpectedUnique(e.Number,e.Message)))
        { throw new InvalidOperationException("Organization code is already in use or account assignment is unavailable.", error); }
    }
    internal static bool IsExpectedUnique(int number,string message) =>
        AssignmentRepository.IsNamedUnique(number,message,"IX_Organizations_Code") ||
        AssignmentRepository.IsNamedUnique(number,message,"IX_OrganizationMemberships_OrganizationId_UserId");
}
