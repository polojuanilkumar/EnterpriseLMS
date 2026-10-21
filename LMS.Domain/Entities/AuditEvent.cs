using System.Text.Json;
using LMS.Domain.Enums;

namespace LMS.Domain.Entities;

public enum AuditAction
{
    OrganizationCreated, MembershipCreated, MembershipRoleChanged, CourseInstructorChanged,
    InvitationCreated, InvitationReissued, InvitationRevoked, InvitationAccepted, InvitationDelivery,
    PasswordReset, CoursePublicationChanged, LessonPublicationChanged, QuizPublicationChanged,
    AssignmentPublicationChanged, AssignmentSubmitted, AssignmentGraded, EnrollmentCancelled, EnrollmentCompleted
}
public enum AuditOutcome { Succeeded, Failed, Unknown }

// Typed factories are the complete metadata allowlist. No request dictionaries are accepted.
public sealed class AuditMetadata
{
    private readonly Dictionary<string,string> values = new();
    private AuditMetadata() { }
    public static AuditMetadata Empty() => new();
    public static AuditMetadata Roles(OrganizationRole? oldRole,OrganizationRole newRole,Guid target)
    {
        if(!Enum.IsDefined(newRole) || oldRole.HasValue && !Enum.IsDefined(oldRole.Value) || target==Guid.Empty)
            throw new ArgumentException("Invalid audit role metadata.");
        var m=new AuditMetadata();if(oldRole.HasValue)m.values["oldRole"]=oldRole.Value.ToString();
        m.values["newRole"]=newRole.ToString();m.values["targetUserId"]=target.ToString("D");return m;
    }
    public static AuditMetadata InvitationRole(OrganizationRole role)
    {
        if(!Enum.IsDefined(role))throw new ArgumentException("Invalid audit role metadata.");
        var m=new AuditMetadata();m.values["newRole"]=role.ToString();return m;
    }
    public static AuditMetadata Reissue(OrganizationRole role,Guid previous)
    {
        if(previous==Guid.Empty)throw new ArgumentException("Invalid audit invitation metadata.");
        var m=InvitationRole(role);m.values["relatedResourceId"]=previous.ToString("D");return m;
    }
    public static AuditMetadata Publication(bool oldValue,bool newValue)
    {var m=new AuditMetadata();m.values["oldPublished"]=oldValue?"true":"false";m.values["newPublished"]=newValue?"true":"false";return m;}
    public static AuditMetadata Instructors(Guid oldId,Guid newId)
    {if(oldId==Guid.Empty||newId==Guid.Empty)throw new ArgumentException("Invalid audit instructor metadata.");var m=new AuditMetadata();m.values["oldInstructorId"]=oldId.ToString("D");m.values["newInstructorId"]=newId.ToString("D");return m;}
    public static AuditMetadata Related(Guid id)
    {if(id==Guid.Empty)throw new ArgumentException("Invalid audit resource metadata.");var m=new AuditMetadata();m.values["relatedResourceId"]=id.ToString("D");return m;}
    internal string Json() => JsonSerializer.Serialize(values);
    public static void Validate(string json)
    {
        if(json.Length>512)throw new ArgumentException("Audit metadata exceeds its limit.");
        using var document=JsonDocument.Parse(json);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Invalid audit metadata.");
        var keys=new HashSet<string>();
        foreach(var property in document.RootElement.EnumerateObject())
        {
            if(!keys.Add(property.Name)||property.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException("Invalid audit metadata.");
            var value=property.Value.GetString();
            var valid=property.Name switch
            {
                "oldRole" or "newRole" => value is "Student" or "Instructor" or "Admin",
                "oldPublished" or "newPublished" => value is "true" or "false",
                "targetUserId" or "oldInstructorId" or "newInstructorId" or "relatedResourceId" => Guid.TryParseExact(value,"D",out var id)&&id!=Guid.Empty,
                _ => false
            };
            if(!valid)throw new ArgumentException("Audit metadata is not allowlisted.");
        }
    }
}

public sealed class AuditEvent
{
    private AuditEvent() { }
    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public string Scope { get; private set; }="";
    public string Action { get; private set; }="";
    public string ResourceType { get; private set; }="";
    public Guid ResourceId { get; private set; }
    public string Outcome { get; private set; }="";
    public string CorrelationId { get; private set; }="";
    public string Metadata { get; private set; }="{}";

    public static string Code(AuditAction action) => action switch
    {
        AuditAction.OrganizationCreated=>"organization.created",AuditAction.MembershipCreated=>"membership.created",
        AuditAction.MembershipRoleChanged=>"membership.role-changed",AuditAction.CourseInstructorChanged=>"course.instructor-changed",
        AuditAction.InvitationCreated=>"invitation.created",AuditAction.InvitationReissued=>"invitation.reissued",
        AuditAction.InvitationRevoked=>"invitation.revoked",AuditAction.InvitationAccepted=>"invitation.accepted",
        AuditAction.InvitationDelivery=>"invitation.delivery",AuditAction.PasswordReset=>"account.password-reset",
        AuditAction.CoursePublicationChanged=>"course.publication-changed",AuditAction.LessonPublicationChanged=>"lesson.publication-changed",
        AuditAction.QuizPublicationChanged=>"quiz.publication-changed",AuditAction.AssignmentPublicationChanged=>"assignment.publication-changed",
        AuditAction.AssignmentSubmitted=>"assignment.submitted",AuditAction.AssignmentGraded=>"assignment.graded",
        AuditAction.EnrollmentCancelled=>"enrollment.cancelled",AuditAction.EnrollmentCompleted=>"enrollment.completed",
        _=>throw new ArgumentException("Invalid audit action.")
    };
    public static string Resource(AuditAction action) => action switch
    {
        AuditAction.OrganizationCreated=>"organization",
        AuditAction.MembershipCreated or AuditAction.MembershipRoleChanged=>"membership",
        AuditAction.CourseInstructorChanged or AuditAction.CoursePublicationChanged=>"course",
        AuditAction.LessonPublicationChanged=>"lesson",AuditAction.QuizPublicationChanged=>"quiz",
        AuditAction.AssignmentPublicationChanged=>"assignment",AuditAction.AssignmentSubmitted=>"assignment-submission",
        AuditAction.AssignmentGraded=>"assignment-grade",
        AuditAction.EnrollmentCancelled or AuditAction.EnrollmentCompleted=>"enrollment",
        AuditAction.PasswordReset=>"account",_ when Enum.IsDefined(action)=>"invitation",
        _=>throw new ArgumentException("Invalid audit action.")
    };
    public static IReadOnlyList<string> Actions => Enum.GetValues<AuditAction>().Select(Code).ToArray();
    public static readonly string[] Outcomes=["succeeded","failed","unknown"];
    public static AuditEvent Create(AuditAction action,Guid resourceId,Guid? organizationId,Guid? actor,
        Guid operationId,string correlationId,DateTimeOffset now,AuditMetadata metadata,AuditOutcome outcome=AuditOutcome.Succeeded)
    {
        var value=new AuditEvent {Id=Guid.NewGuid(),OperationId=operationId,TimestampUtc=now.ToUniversalTime(),ActorUserId=actor,
            OrganizationId=organizationId,Scope=action==AuditAction.PasswordReset?"account":"organization",Action=Code(action),
            ResourceType=Resource(action),ResourceId=resourceId,Outcome=outcome.ToString().ToLowerInvariant(),CorrelationId=correlationId,Metadata=metadata.Json()};
        value.Validate();return value;
    }
    public void Validate()
    {
        if(Id==Guid.Empty||OperationId==Guid.Empty||ResourceId==Guid.Empty||ActorUserId==Guid.Empty||OrganizationId==Guid.Empty
            ||TimestampUtc.Offset!=TimeSpan.Zero||!Guid.TryParseExact(CorrelationId,"N",out _)
            ||!Actions.Contains(Action)||!Outcomes.Contains(Outcome)
            ||(Scope=="organization" ? !OrganizationId.HasValue : Scope!="account" || OrganizationId.HasValue)
            ||ResourceType!=Resource(Enum.GetValues<AuditAction>().Single(x=>Code(x)==Action))
            ||Scope!=(Action=="account.password-reset"?"account":"organization")
            ||Action!="invitation.delivery" && Outcome!="succeeded")throw new ArgumentException("Invalid audit event.");
        AuditMetadata.Validate(Metadata);
        using var json=JsonDocument.Parse(Metadata);
        var allowed=Action switch
        {
            "membership.created" or "membership.role-changed"=>new[]{"oldRole","newRole","targetUserId"},
            "invitation.created"=>new[]{"newRole"},"invitation.reissued"=>new[]{"newRole","relatedResourceId"},
            "course.instructor-changed"=>new[]{"oldInstructorId","newInstructorId"},
            "course.publication-changed" or "lesson.publication-changed" or "quiz.publication-changed" or "assignment.publication-changed"=>new[]{"oldPublished","newPublished"},
            "invitation.accepted" or "assignment.submitted" or "assignment.graded" or "enrollment.cancelled" or "enrollment.completed"=>new[]{"relatedResourceId"},
            _=>Array.Empty<string>()
        };
        if(json.RootElement.EnumerateObject().Any(p=>!allowed.Contains(p.Name)))throw new ArgumentException("Metadata does not match the audit action.");
    }
}
