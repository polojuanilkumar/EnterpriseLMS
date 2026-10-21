using LMS.Domain.Entities;
namespace LMS.Application.Features.Auditing;

public sealed class AuditRequestContext
{
    public Guid? ActorUserId { get; private set; }
    public Guid OperationId { get; }=Guid.NewGuid();
    public string CorrelationId { get; }=Guid.NewGuid().ToString("N");
    // Called only after JWT validation and server-side organization middleware verification.
    public void Authenticate(Guid actor){if(actor==Guid.Empty)throw new ArgumentException("Invalid audit actor.");ActorUserId=actor;}
}
public interface IAuditWriter
{
    void Stage(AuditAction action,Guid resourceId,Guid? storedOrganizationId,AuditMetadata metadata);
}
public interface IAuditObservationWriter
{
    Task AppendDelivery(AuditEvent observedEvent,CancellationToken token);
}
public sealed class AuditFilter
{
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=20;
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public string? Action { get; set; }
    public string? Outcome { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? ResourceId { get; set; }
    public AuditFilter Normalize(DateTimeOffset now)
    {
        var to=ToUtc??now.ToUniversalTime();var from=FromUtc??to.AddDays(-7);
        if(Page is <1 or >10000||PageSize is <1 or >50||from.Offset!=TimeSpan.Zero||to.Offset!=TimeSpan.Zero||to<=from||to-from>TimeSpan.FromDays(31)
            ||Action is not null&&!AuditEvent.Actions.Contains(Action)||Outcome is not null&&!AuditEvent.Outcomes.Contains(Outcome)
            ||ActorId==Guid.Empty||ResourceId==Guid.Empty)throw new ArgumentException("Audit filters require a UTC range of at most 31 days and allowlisted action/outcome values.");
        return new() {Page=Page,PageSize=PageSize,FromUtc=from,ToUtc=to,Action=Action,Outcome=Outcome,ActorId=ActorId,ResourceId=ResourceId};
    }
}
public sealed record AuditRow(Guid Id,DateTimeOffset TimestampUtc,Guid? ActorUserId,Guid? OrganizationId,
    string Scope,string Action,string ResourceType,Guid ResourceId,string Outcome,string CorrelationId,string Metadata);
public sealed record AuditPage(Guid? OrganizationId,int Page,int PageSize,int Total,AuditFilter Filter,AuditRow[] Items);
public interface IAuditQueryRepository
{
    Task<AuditPage> Organization(Guid id,AuditFilter filter,CancellationToken token);
    Task<AuditRow?> OrganizationDetail(Guid organizationId,Guid eventId,CancellationToken token);
    Task<AuditPage> Platform(AuditFilter filter,CancellationToken token);
    Task<AuditRow?> PlatformDetail(Guid eventId,CancellationToken token);
}
