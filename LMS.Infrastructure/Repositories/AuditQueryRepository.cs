using LMS.Application.Features.Auditing;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;
public sealed class AuditQueryRepository(LMSDbContext db,OrganizationContext selected,AuditRequestContext request,TimeProvider clock) : IAuditQueryRepository
{
    private Guid? Actor=>request.ActorUserId;
    private Task<bool> StoredPlatform(CancellationToken token)=> (from ur in db.UserRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
        where ur.UserId==Actor&&r.NormalizedName=="SUPERADMIN" select ur.UserId).AnyAsync(token);
    private async Task RequireOrganization(Guid id,CancellationToken token)
    {
        // Reject route/context mismatch before accessing the supplied organization at all.
        if(selected.OrganizationId!=id||selected.UserId!=Actor||Actor is null)throw new UnauthorizedAccessException("Explicit target organization selection is required.");
        var active=await db.Organizations.AsNoTracking().AnyAsync(x=>x.Id==id&&x.IsActive,token);
        var role=await db.OrganizationMemberships.AsNoTracking().Where(x=>x.OrganizationId==id&&x.UserId==Actor).Select(x=>(OrganizationRole?)x.Role).SingleOrDefaultAsync(token);
        AuditAccess.Organization(selected.OrganizationId,id,Actor,active,await StoredPlatform(token),role);
    }
    private async Task<AuditPage> Page(IQueryable<AuditEvent> scope,Guid? org,AuditFilter filter,CancellationToken token)
    {
        var normalized=filter.Normalize(clock.GetUtcNow());var filtered=AuditQueries.Filter(scope,normalized);
        var total=await filtered.CountAsync(token);
        var rows=await AuditQueries.Rows(AuditQueries.Ordered(filtered).Skip((normalized.Page-1)*normalized.PageSize).Take(normalized.PageSize)).ToArrayAsync(token);
        return new(org,normalized.Page,normalized.PageSize,total,normalized,rows);
    }
    public async Task<AuditPage> Organization(Guid id,AuditFilter filter,CancellationToken token)
    {await RequireOrganization(id,token);return await Page(AuditQueries.Organization(db.AuditEvents.AsNoTracking(),id),id,filter,token);}
    public async Task<AuditRow?> OrganizationDetail(Guid organizationId,Guid eventId,CancellationToken token)
    {await RequireOrganization(organizationId,token);return await AuditQueries.Rows(AuditQueries.Organization(db.AuditEvents.AsNoTracking(),organizationId).Where(x=>x.Id==eventId)).SingleOrDefaultAsync(token);}
    public async Task<AuditPage> Platform(AuditFilter filter,CancellationToken token)
    {AuditAccess.Platform(Actor,await StoredPlatform(token));return await Page(AuditQueries.Platform(db.AuditEvents.AsNoTracking()),null,filter,token);}
    public async Task<AuditRow?> PlatformDetail(Guid eventId,CancellationToken token)
    {AuditAccess.Platform(Actor,await StoredPlatform(token));return await AuditQueries.Rows(AuditQueries.Platform(db.AuditEvents.AsNoTracking()).Where(x=>x.Id==eventId)).SingleOrDefaultAsync(token);}
}
