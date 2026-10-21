using LMS.Application.Features.Auditing;
using LMS.Domain.Entities;
namespace LMS.Infrastructure.Repositories;
public static class AuditQueries
{
    public static IQueryable<AuditEvent> Organization(IQueryable<AuditEvent> events,Guid organization)
    {
        if(organization==Guid.Empty)throw new ArgumentException("Select an audit organization.");
        return events.Where(x=>x.Scope=="organization"&&x.OrganizationId==organization);
    }
    public static IQueryable<AuditEvent> Platform(IQueryable<AuditEvent> events)
        =>events.Where(x=>x.OrganizationId==null&&(x.Scope=="platform"||x.Scope=="account"));
    public static IQueryable<AuditEvent> Filter(IQueryable<AuditEvent> events,AuditFilter f)
    {
        // Repository normalizes and validates once before applying filters/count/pagination.
        events=events.Where(x=>x.TimestampUtc>=f.FromUtc!.Value&&x.TimestampUtc<f.ToUtc!.Value);
        if(f.Action is not null)events=events.Where(x=>x.Action==f.Action);
        if(f.Outcome is not null)events=events.Where(x=>x.Outcome==f.Outcome);
        if(f.ActorId.HasValue)events=events.Where(x=>x.ActorUserId==f.ActorId);
        if(f.ResourceId.HasValue)events=events.Where(x=>x.ResourceId==f.ResourceId);
        return events;
    }
    public static IQueryable<AuditRow> Rows(IQueryable<AuditEvent> events)
        =>events.Select(x=>new AuditRow(x.Id,x.TimestampUtc,x.ActorUserId,x.OrganizationId,x.Scope,x.Action,x.ResourceType,x.ResourceId,x.Outcome,x.CorrelationId,x.Metadata));
    public static IOrderedQueryable<AuditEvent> Ordered(IQueryable<AuditEvent> events)
        =>events.OrderByDescending(x=>x.TimestampUtc).ThenByDescending(x=>x.Id);
}
