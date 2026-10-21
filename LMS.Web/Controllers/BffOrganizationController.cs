using System.ComponentModel.DataAnnotations;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LMS.Web.Controllers;
[ApiController,Authorize,AutoValidateAntiforgeryToken,Route("bff/organization"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BffOrganizationController(LearnerApi api,LearnerSessions sessions):ControllerBase
{
    private LearnerSession Current(){var s=sessions.Require(User);if(!s.OrganizationId.HasValue)throw new WebApiException(403);return s;}
    [HttpGet("members")]
    public Task<MembersPage> Members([FromQuery]OrganizationFilterForm filter,CancellationToken token){var s=Current();return api.Members(s,s.OrganizationId!.Value,filter,token);}
    [HttpPost("members"),HttpPut("members/{userId:guid}"),EnableRateLimiting("organization-administration")]
    public Task<IActionResult> Member([FromRoute]Guid? userId,BffMemberChange form,CancellationToken token)=>Change(()=>api.BffSetMember(Current(),userId,form,token));
    [HttpGet("courses/{courseId:guid}/instructor")]
    public Task<InstructorPage> Choices(Guid courseId,[FromQuery]OrganizationFilterForm filter,CancellationToken token){var s=Current();return api.InstructorChoices(s,s.OrganizationId!.Value,courseId,token,filter);}
    [HttpPut("courses/{courseId:guid}/instructor"),EnableRateLimiting("organization-administration")]
    public Task<IActionResult> Instructor(Guid courseId,BffInstructorChange form,CancellationToken token)=>Change(()=>api.BffAssignInstructor(Current(),courseId,form.InstructorId,form.Confirmed,token));
    [HttpGet("invitations"),EnableRateLimiting("organization-administration")]
    public Task<WebInvitationPage> Invitations(int page=1,int pageSize=20,CancellationToken token=default)=>api.BffInvitations(Current(),page,pageSize,token);
    [HttpPost("invitations"),EnableRateLimiting("organization-administration")]
    public Task<IActionResult> Invite(InviteForm form,CancellationToken token)=>Change(()=>api.BffInvite(Current(),form,token));
    [HttpPost("invitations/{invitationId:guid}/revoke"),EnableRateLimiting("organization-administration")]
    public Task<IActionResult> Revoke(Guid invitationId,StaffConfirmation form,CancellationToken token)=>Change(()=>api.BffRevokeInvitation(Current(),invitationId,form.Confirmed,token));
    [HttpGet("audit")]
    public async Task<object> Audit([FromQuery]AuditViewFilter filter,CancellationToken token)
    {var s=Current();var p=await api.AuditPage(s,s.OrganizationId,filter,token);p.Filter.Validate();if(!p.Filter.FromUtc.HasValue||!p.Filter.ToUtc.HasValue)throw new WebApiException(502);return new {p.Page,p.PageSize,p.Total,p.Filter,Items=p.Items.Select(LearnerApi.BffAudit).ToArray(),Actions=AuditDisplay.Actions,Outcomes=AuditDisplay.Outcomes};}
    [HttpGet("audit/{eventId:guid}")]
    public async Task<BffAuditRow> AuditDetail(Guid eventId,CancellationToken token){var s=Current();return LearnerApi.BffAudit(await api.AuditDetail(s,s.OrganizationId,eventId,token));}
    private async Task<IActionResult> Change(Func<Task> action)
    {
        try{await action();return NoContent();}
        catch(WebApiException ex) when(ex.Status is 400 or 409)
        {return StatusCode(ex.Status,new BffError(ex.Status==409?"conflict":"validation_failed",ex.SafeMessage??"Account unavailable or membership/invitation conflicts with current state. Check confirmation, exact email and role; refresh stored state before another action."));}
    }
}
public sealed class BffInstructorChange
{[Required]public Guid InstructorId{get;set;}public bool Confirmed{get;set;}}

[ApiController,Authorize,Route("bff/staff/reports/courses"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class BffStaffReportsController(LearnerApi api,LearnerSessions sessions):ControllerBase
{
    private LearnerSession Current(){var s=sessions.Require(User);if(!s.OrganizationId.HasValue)throw new WebApiException(403);return s;}
    [HttpGet]public async Task<object> Courses([FromQuery]ReportFilterView filter,CancellationToken token)
    {
        var s=Current();var p=await api.StaffReports(s,s.OrganizationId!.Value,filter,token);
        return new {p.OrganizationId,p.Filter,Courses=new {p.Courses.OrganizationId,p.Courses.OwnerUserId,p.Courses.Page,p.Courses.PageSize,p.Courses.Total,p.Courses.Pages,Items=p.Courses.Items.Select(c=>new {
            c.CourseId,c.OrganizationId,c.InstructorId,c.Title,c.Published,c.Enrollments,c.Active,c.Completed,c.Cancelled,c.PublishedLessons,
            c.QuizStarted,c.QuizSubmitted,c.QuizExpired,c.QuizPassed,c.QuizFailed,c.QuizUnavailable,c.Assignments,c.Submissions,c.Graded,c.Ungraded,
            ProgressDenominator=c.ProgressDenominator.ToString(System.Globalization.CultureInfo.InvariantCulture),c.CompletedPublishedLearnerLessons,c.CurrentPercentage}).ToArray()}};
    }
    [HttpGet("{courseId:guid}/learners")]
    public Task<StaffLearnersReportPage> Learners(Guid courseId,[FromQuery]ReportFilterView filter,CancellationToken token){var s=Current();return api.StaffReportLearners(s,s.OrganizationId!.Value,courseId,filter,token);}
    [HttpGet("{courseId:guid}/learners/{userId:guid}/assignment-review")]
    public Task<ReportSubmissionLink> Review(Guid courseId,Guid userId,Guid sectionId,Guid lessonId,Guid assignmentId,Guid submissionId,CancellationToken token)
        =>api.BffReportReview(Current(),courseId,userId,new(courseId,sectionId,lessonId,assignmentId,submissionId),token);
}
