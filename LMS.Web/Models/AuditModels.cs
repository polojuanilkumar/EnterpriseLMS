namespace LMS.Web.Models;
public sealed class AuditViewFilter
{
    public int Page { get; set; }=1;
    public int PageSize { get; set; }=20;
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public string? Action { get; set; }
    public string? Outcome { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? ResourceId { get; set; }
    public void Validate()
    {
        if(Page is <1 or >10000||PageSize is <1 or >50||FromUtc?.Offset!=null&&FromUtc.Value.Offset!=TimeSpan.Zero
            ||ToUtc?.Offset!=null&&ToUtc.Value.Offset!=TimeSpan.Zero||FromUtc.HasValue&&ToUtc.HasValue&&(ToUtc<=FromUtc||ToUtc-FromUtc>TimeSpan.FromDays(31))
            ||Action is not null&&!AuditDisplay.Actions.Contains(Action)||Outcome is not null&&!AuditDisplay.Outcomes.Contains(Outcome)
            ||ActorId==Guid.Empty||ResourceId==Guid.Empty)throw new Services.WebApiException(400);
    }
}
public static class AuditDisplay
{
    public static readonly string[] Actions=["organization.created","membership.created","membership.role-changed","course.instructor-changed",
        "invitation.created","invitation.reissued","invitation.revoked","invitation.accepted","invitation.delivery","account.password-reset",
        "course.publication-changed","lesson.publication-changed","quiz.publication-changed","assignment.publication-changed",
        "assignment.submitted","assignment.graded","enrollment.cancelled","enrollment.completed"];
    public static readonly string[] Outcomes=["succeeded","failed","unknown"];
}
public sealed record AuditViewRow(Guid Id,DateTimeOffset TimestampUtc,Guid? ActorUserId,Guid? OrganizationId,string Scope,
    string Action,string ResourceType,Guid ResourceId,string Outcome,string CorrelationId,string Metadata);
public sealed record AuditViewPage(Guid? OrganizationId,int Page,int PageSize,int Total,AuditViewFilter Filter,AuditViewRow[] Items);
