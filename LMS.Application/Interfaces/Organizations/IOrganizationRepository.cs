using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Application.Features.Organizations;

namespace LMS.Application.Interfaces.Organizations;

public interface IOrganizationRepository
{
    Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken token = default);
    Task<bool> UserExistsAsync(Guid userId, CancellationToken token = default);
    Task<Organization?> GetAsync(Guid id, CancellationToken token = default);
    Task<OrganizationMembership?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken token = default);
    Task<IReadOnlyList<Organization>> ListForUserAsync(Guid userId, CancellationToken token = default);
    Task AddAsync(Organization organization, OrganizationMembership initialAdmin, CancellationToken token = default);
    Task SetMembershipAsync(Guid organizationId, Guid userId, OrganizationRole role, CancellationToken token = default);
    Task<OrganizationListPage<OrganizationMemberRow>> MembersPageAsync(Guid organizationId, OrganizationListFilter filter, CancellationToken token = default);
    Task<OrganizationListPage<PlatformOrganizationRow>> PlatformPageAsync(OrganizationListFilter filter, CancellationToken token = default);
    Task<Guid?> FindAssignableByEmailAsync(string normalizedEmail, CancellationToken token = default);
    Task AddMemberByEmailAsync(Guid organizationId, string normalizedEmail, OrganizationRole role, CancellationToken token = default);
    Task<OrganizationMemberRow?> MemberAsync(Guid organizationId, Guid userId, CancellationToken token = default);
}
