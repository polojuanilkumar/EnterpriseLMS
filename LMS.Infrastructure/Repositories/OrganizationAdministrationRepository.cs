using System.Data;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LMS.Infrastructure.Repositories;

public sealed partial class OrganizationRepository
{
    public async Task<OrganizationMemberRow?> MemberAsync(Guid organizationId, Guid userId, CancellationToken token = default)
    {
        await RequireStoredAdmin(organizationId,token);
        return await OrganizationAdministrationQueries.Members(db.OrganizationMemberships.AsNoTracking(),db.Users.AsNoTracking(),organizationId,new())
            .SingleOrDefaultAsync(x=>x.UserId==userId,token);
    }
    public async Task<OrganizationListPage<OrganizationMemberRow>> MembersPageAsync(Guid organizationId, OrganizationListFilter filter, CancellationToken token = default)
    {
        // Context is verified by middleware, then rechecked against stored membership on each call.
        await RequireStoredAdmin(organizationId, token);
        var query = OrganizationAdministrationQueries.Members(db.OrganizationMemberships.AsNoTracking(), db.Users.AsNoTracking(), organizationId, filter);
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.UserId)
            .Skip(filter.Skip).Take(filter.PageSize).ToArrayAsync(token);
        return new(organizationId, filter.Page, filter.PageSize, total, rows);
    }
    public async Task<OrganizationListPage<PlatformOrganizationRow>> PlatformPageAsync(OrganizationListFilter filter, CancellationToken token = default)
    {
        var query = OrganizationAdministrationQueries.Organizations(db.Organizations.AsNoTracking(), filter);
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip(filter.Skip).Take(filter.PageSize).ToArrayAsync(token);
        return new(null, filter.Page, filter.PageSize, total, rows);
    }
    public async Task<Guid?> FindAssignableByEmailAsync(string normalizedEmail, CancellationToken token = default)
    {
        var ids = await OrganizationAdministrationQueries.ExactAccount(db.Users.AsNoTracking(), normalizedEmail).ToArrayAsync(token);
        // Identity's EmailIndex is not unique. Ambiguous matches never choose an arbitrary account.
        return ids.Length == 1 && !await IsSuperAdminAsync(ids[0], token) ? ids[0] : null;
    }
    public Task AddMemberByEmailAsync(Guid organizationId, string normalizedEmail, OrganizationRole role, CancellationToken token = default)
        => ChangeMembership(organizationId, null, normalizedEmail, role, true, token);

    private async Task RequireStoredAdmin(Guid organizationId, CancellationToken token)
    {
        if (context.OrganizationId != organizationId || context.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Select a verified organization.");
        if (!await db.Organizations.AnyAsync(x => x.Id == organizationId && x.IsActive, token))
            throw new UnauthorizedAccessException("Organization access denied.");
        if (!await IsSuperAdminAsync(context.UserId, token) && !await db.OrganizationMemberships.AnyAsync(x =>
                x.OrganizationId == organizationId && x.UserId == context.UserId && x.Role == OrganizationRole.Admin, token))
            throw new UnauthorizedAccessException("Stored organization Admin authorization is required.");
    }

    private async Task ChangeMembership(Guid organizationId, Guid? userId, string? email, OrganizationRole role, bool createOnly, CancellationToken token)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid organization role.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        // Shared lock protocol with course Instructor and Active-enrollment writes.
        await db.OrganizationWriteLock(organizationId).ToListAsync(token);
        await RequireStoredAdmin(organizationId, token);
        if(email is not null)
        {
            var resource="lms-account:"+LMS.Infrastructure.Identity.OrganizationInvitation.Hash(email);
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Account operation busy.', 1;",token);
        }
        var target = userId ?? await FindAssignableByEmailAsync(email!, token);
        if (!target.HasValue || !await UserExistsAsync(target.Value, token) || await IsSuperAdminAsync(target.Value, token))
            throw new InvalidOperationException(MembershipRules.Unavailable);
        // AsNoTracking avoids a membership cached during middleware bypassing the locked read.
        var existing = await db.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == target.Value, token);
        if (createOnly && existing is not null) throw new InvalidOperationException(MembershipRules.Unavailable);
        if (existing is null)
        {
            var membership=new OrganizationMembership(organizationId,target.Value,role);
            db.OrganizationMemberships.Add(membership);
            db.Stage(AuditAction.MembershipCreated,membership.Id,organizationId,AuditMetadata.Roles(null,role,target.Value));
        }
        else
        {
            if (context.UserId == target.Value && existing.Role == OrganizationRole.Admin && role != OrganizationRole.Admin)
                throw new InvalidOperationException("An organization Admin cannot demote their own membership.");
            var admins = await OrganizationAdministrationQueries.Admins(db.OrganizationMemberships,organizationId).CountAsync(token);
            // IgnoreQueryFilters is intentional here; explicit stored parent organization replaces them.
            var assigned = await OrganizationAdministrationQueries.AssignedCourses(db.Courses.IgnoreQueryFilters(),organizationId,target.Value).AnyAsync(token);
            var active = await OrganizationAdministrationQueries.ActiveEnrollments(db.CourseEnrollments.IgnoreQueryFilters(),db.Courses.IgnoreQueryFilters(),organizationId,target.Value).AnyAsync(token);
            MembershipRules.Check(existing.Role, role, admins, assigned, active);
            var tracked = db.OrganizationMemberships.Local.SingleOrDefault(x => x.Id == existing.Id);
            if (tracked is not null) db.Entry(tracked).State = EntityState.Detached;
            var oldRole=existing.Role;
            existing.ChangeRole(role); db.OrganizationMemberships.Update(existing);
            if(oldRole!=role)db.Stage(AuditAction.MembershipRoleChanged,existing.Id,organizationId,AuditMetadata.Roles(oldRole,role,target.Value));
        }
        await SaveAsync(token);
        await transaction.CommitAsync(token);
    }
}
