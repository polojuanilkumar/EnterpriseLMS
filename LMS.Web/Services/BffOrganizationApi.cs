using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public Task OrganizationAdmin(LearnerSession s,CancellationToken token)=>StaffAccessOnly(s,token);
    private async Task StaffAccessOnly(LearnerSession s,CancellationToken token)=>await StaffAccess(s,s.OrganizationId??throw new WebApiException(403),true,token);
    public async Task BffSetMember(LearnerSession s,Guid? user,BffMemberChange form,CancellationToken token)
    {
        await OrganizationAdmin(s,token);var org=s.OrganizationId!.Value;
        if(!form.Confirmed)throw new WebApiException(400);
        if(user.HasValue)await OrganizationMember(s,org,user.Value,token);
        else if(string.IsNullOrWhiteSpace(form.Email)||!System.Net.Mail.MailAddress.TryCreate(form.Email.Trim(),out var address)||address.Address!=form.Email.Trim())throw new WebApiException(400,"Enter an exact email address, without a display name.");
        await SetMember(s,org,new MemberForm{UserId=user??Guid.Empty,Email=user.HasValue?null:form.Email!.Trim(),Role=form.Role,Confirmed=true},token);
    }
    public async Task<WebInvitationPage> BffInvitations(LearnerSession s,int page,int size,CancellationToken token)
    {
        await OrganizationAdmin(s,token);
        if(page is <1 or >100000||size is <1 or >100)throw new WebApiException(400);
        var org=s.OrganizationId!.Value;
        var result=await AccountRequest<WebInvitationPage>($"api/account/organizations/{org}/invitations?page={page}&pageSize={size}",null,s,org,token,true)??throw new WebApiException(502);
        if(result.Page!=page||result.PageSize!=size||result.Total<0||result.Items.Length>size||result.Items.Any(x=>x.OrganizationId!=org||x.Id==Guid.Empty||x.Role is <1 or >3)||result.Items.Select(x=>x.Id).Distinct().Count()!=result.Items.Length)throw new WebApiException(502);
        return result;
    }
    public async Task BffInvite(LearnerSession s,InviteForm form,CancellationToken token)
    {
        await OrganizationAdmin(s,token);if(!form.Confirmed)throw new WebApiException(400);
        await AccountRequest<JsonElement>($"api/account/organizations/{s.OrganizationId}/invitations",form,s,s.OrganizationId,token);
    }
    public async Task BffRevokeInvitation(LearnerSession s,Guid id,bool confirmed,CancellationToken token)
    {
        await OrganizationAdmin(s,token);if(!confirmed)throw new WebApiException(400);
        // There is no invitation-detail API. The existing revoke API resolves ID + selected org under its write lock.
        await AccountRequest<JsonElement>($"api/account/organizations/{s.OrganizationId}/invitations/{id}/revoke",new {confirmed=true},s,s.OrganizationId,token);
    }
    public async Task BffAssignInstructor(LearnerSession s,Guid course,Guid instructor,bool confirmed,CancellationToken token)
    {
        await OrganizationAdmin(s,token);if(!confirmed)throw new WebApiException(400);
        var member=await OrganizationMember(s,s.OrganizationId!.Value,instructor,token);if(member.Role!=2)throw new WebApiException(400);
        await AssignInstructor(s,s.OrganizationId.Value,course,instructor,token);
    }
    public async Task<ReportSubmissionLink> BffReportReview(LearnerSession s,Guid course,Guid user,ReportSubmissionLink link,CancellationToken token)
    {
        if(link.CourseId!=course)throw new WebApiException(404);
        var page=await StaffAssignment(s,course,link.SectionId,link.LessonId,link.AssignmentId,link.SubmissionId,token);
        if(page.Submission?.UserId!=user)throw new WebApiException(404);return link;
    }
    public static BffAuditRow BffAudit(AuditViewRow row)
    {
        var keys=row.Action switch {
            "membership.created" or "membership.role-changed"=>new[]{"oldRole","newRole","targetUserId"},
            "invitation.created"=>["newRole"],"invitation.reissued"=>["newRole","relatedResourceId"],
            "course.instructor-changed"=>["oldInstructorId","newInstructorId"],
            "course.publication-changed" or "lesson.publication-changed" or "quiz.publication-changed" or "assignment.publication-changed"=>["oldPublished","newPublished"],
            "invitation.accepted" or "assignment.submitted" or "assignment.graded" or "enrollment.cancelled" or "enrollment.completed"=>["relatedResourceId"],_=>Array.Empty<string>()};
        var resource=row.Action switch {"organization.created"=>"organization","membership.created" or "membership.role-changed"=>"membership","course.instructor-changed" or "course.publication-changed"=>"course","lesson.publication-changed"=>"lesson","quiz.publication-changed"=>"quiz","assignment.publication-changed"=>"assignment","assignment.submitted"=>"assignment-submission","assignment.graded"=>"assignment-grade","enrollment.cancelled" or "enrollment.completed"=>"enrollment",_=>"invitation"};
        if(row.Action=="account.password-reset"||row.Scope!="organization"||row.OrganizationId is null||row.Id==Guid.Empty||row.ResourceId==Guid.Empty||row.ActorUserId==Guid.Empty||row.TimestampUtc.Offset!=TimeSpan.Zero||row.ResourceType!=resource||!Guid.TryParseExact(row.CorrelationId,"N",out _)||row.Metadata.Length>512)throw new WebApiException(502);
        var metadata=new Dictionary<string,string>();
        try
        {
            using var json=JsonDocument.Parse(row.Metadata);
            if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new WebApiException(502);
            foreach(var property in json.RootElement.EnumerateObject())
            {
                if(!keys.Contains(property.Name)||property.Value.ValueKind!=JsonValueKind.String)throw new WebApiException(502);
                var value=property.Value.GetString()!;
                var valid=property.Name switch {"oldRole" or "newRole"=>value is "Student" or "Instructor" or "Admin","oldPublished" or "newPublished"=>value is "true" or "false",_=>Guid.TryParseExact(value,"D",out var id)&&id!=Guid.Empty};
                if(!valid||!metadata.TryAdd(property.Name,value))throw new WebApiException(502);
            }
        }
        catch(JsonException){throw new WebApiException(502);}
        return new(row.Id,row.TimestampUtc,row.ActorUserId,row.Action,row.ResourceType,row.ResourceId,row.Outcome,row.CorrelationId,metadata);
    }
}
public sealed class BffMemberChange
{
    [StringLength(256)]public string? Email{get;set;}
    [Range(1,3)]public int Role{get;set;}
    public bool Confirmed{get;set;}
}
public sealed record BffAuditRow(Guid Id,DateTimeOffset TimestampUtc,Guid? ActorUserId,string Action,string ResourceType,Guid ResourceId,string Outcome,string CorrelationId,IReadOnlyDictionary<string,string> Metadata);
