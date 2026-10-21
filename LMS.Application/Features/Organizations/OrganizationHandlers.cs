using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;

namespace LMS.Application.Features.Organizations;

public sealed class OrganizationHandlers(IOrganizationRepository repository, OrganizationContext context)
{
    public async Task SelectAsync(Guid organizationId, Guid userId, CancellationToken token = default)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException("Authenticated user is required.");
        var organization = await repository.GetAsync(organizationId, token);
        if (organization is null || !organization.IsActive)
            throw new UnauthorizedAccessException("Organization access denied.");
        var membership = await repository.GetMembershipAsync(organizationId, userId, token);
        var platform = await repository.IsSuperAdminAsync(userId, token);
        if (membership is null && !platform) throw new UnauthorizedAccessException("Organization access denied.");
        context.Initialize(organizationId, userId, membership?.Role, platform);
    }

    public SelectedOrganizationResponse Current(Guid organizationId)
    {
        if (context.OrganizationId != organizationId || context.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("Select a verified organization.");
        return new(organizationId, context.UserId, context.Role?.ToString(), context.IsSuperAdmin);
    }

    public Task<IReadOnlyList<Organization>> ListAsync(Guid userId, CancellationToken token = default)
        => repository.ListForUserAsync(userId, token);

    public async Task<PlatformAccessResponse> PlatformAccessAsync(Guid actor, CancellationToken token = default)
    {
        if (actor == Guid.Empty || !await repository.IsSuperAdminAsync(actor, token))
            throw new UnauthorizedAccessException("Stored platform SuperAdmin authorization is required.");
        return new(actor, true);
    }
    public async Task<OrganizationListPage<PlatformOrganizationRow>> PlatformPageAsync(Guid actor, OrganizationListFilter filter, CancellationToken token = default)
    {
        await PlatformAccessAsync(actor, token); filter.Validate(false);
        return await repository.PlatformPageAsync(filter, token);
    }
    public Task<OrganizationListPage<OrganizationMemberRow>> MembersPageAsync(Guid organizationId, OrganizationListFilter filter, CancellationToken token = default)
    {
        RequireAdmin(organizationId); filter.Validate(true);
        return repository.MembersPageAsync(organizationId, filter, token);
    }
    public async Task<OrganizationMemberRow> MemberAsync(Guid organizationId, Guid userId, CancellationToken token = default)
    {
        RequireAdmin(organizationId);
        var row=await repository.MemberAsync(organizationId,userId,token);
        if(row is null || row.OrganizationId!=organizationId || row.UserId!=userId)throw new KeyNotFoundException("Organization member not found.");
        return row;
    }
    public async Task<Organization> CreateByEmailAsync(Guid actor, string code, string name, string email, bool confirmed, CancellationToken token = default)
    {
        await PlatformAccessAsync(actor, token);
        if (!confirmed) throw new ArgumentException("Confirm organization creation and initial Admin assignment.");
        // Validate organization fields before looking up an account.
        _ = new Organization(code, name);
        var user = await repository.FindAssignableByEmailAsync(MembershipRules.NormalizeEmail(email), token);
        if (!user.HasValue) throw new InvalidOperationException(MembershipRules.Unavailable);
        try { return await CreateAsync(actor, code, name, user.Value, token); }
        catch(KeyNotFoundException) { throw new InvalidOperationException(MembershipRules.Unavailable); }
    }
    public Task AddMemberByEmailAsync(Guid organizationId, string email, OrganizationRole role, bool confirmed, CancellationToken token = default)
    {
        RequireAdmin(organizationId);
        if (!confirmed || !Enum.IsDefined(role)) throw new ArgumentException("Confirm membership creation and select a valid role.");
        return repository.AddMemberByEmailAsync(organizationId, MembershipRules.NormalizeEmail(email), role, token);
    }

    public async Task<Organization> CreateAsync(Guid actor, string code, string name, Guid initialAdminUserId,
        CancellationToken token = default)
    {
        if (!await repository.IsSuperAdminAsync(actor, token))
            throw new UnauthorizedAccessException("Only platform SuperAdmin can create organizations.");
        if (!await repository.UserExistsAsync(initialAdminUserId, token))
            throw new KeyNotFoundException("Initial admin user not found.");
        var organization = new Organization(code, name);
        await repository.AddAsync(organization,
            new OrganizationMembership(organization.Id, initialAdminUserId, OrganizationRole.Admin), token);
        return organization;
    }

    public async Task SetMemberAsync(Guid organizationId, Guid userId, OrganizationRole role, CancellationToken token = default)
    {
        RequireAdmin(organizationId);
        if (userId == Guid.Empty || !Enum.IsDefined(role))
            throw new ArgumentException("A user ID and valid organization role are required.");
        if (!await repository.UserExistsAsync(userId, token)) throw new InvalidOperationException(MembershipRules.Unavailable);
        // Protect the acting admin from accidentally removing their own administration access.
        if (context.UserId == userId && context.Role == OrganizationRole.Admin && role != OrganizationRole.Admin)
            throw new InvalidOperationException("An organization admin cannot demote their own membership.");
        await repository.SetMembershipAsync(organizationId, userId, role, token);
    }

    private void RequireAdmin(Guid organizationId)
    {
        if (organizationId == Guid.Empty || context.UserId == Guid.Empty || context.OrganizationId != organizationId || (!context.IsSuperAdmin && context.Role != OrganizationRole.Admin))
            throw new UnauthorizedAccessException("Admin access to the selected organization is required.");
    }
}

public sealed record SelectedOrganizationResponse(Guid OrganizationId, Guid UserId, string? Role, bool IsSuperAdmin);
