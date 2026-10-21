using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace LMS.Web.Controllers;

[Authorize,AutoValidateAntiforgeryToken,Route("admin/{organizationId:guid}")]
public sealed class AdminController(LearnerApi api,LearnerSessions sessions) : Controller
{
    private LearnerSession Session(Guid org)=>sessions.Require(User,org);
    [HttpGet]
    public async Task<IActionResult> Index(Guid organizationId,CancellationToken token)
    {
        var access=await api.Access(Session(organizationId),organizationId,token);
        if(!access.IsAdmin)throw new WebApiException(403);
        return View(access);
    }
    [HttpGet("members")]
    public async Task<IActionResult> Members(Guid organizationId,[FromQuery] OrganizationFilterForm filter,CancellationToken token)
    {
        if(!ModelState.IsValid)throw new WebApiException(400);
        return View(await api.Members(Session(organizationId),organizationId,filter,token));
    }
    [HttpPost("members"),EnableRateLimiting("organization-administration")]
    public async Task<IActionResult> SetMember(Guid organizationId,MemberForm form,CancellationToken token)
    {
        var session=Session(organizationId);
        var access=await api.Access(session,organizationId,token);
        if(!access.IsAdmin)throw new WebApiException(403);
        if(!form.Confirmed)ModelState.AddModelError("Confirmed","Confirm membership creation or the role change.");
        if(form.UserId==Guid.Empty && (string.IsNullOrWhiteSpace(form.Email) || !System.Net.Mail.MailAddress.TryCreate(form.Email.Trim(),out var address) || address.Address!=form.Email.Trim()))
            ModelState.AddModelError("Email","Enter an exact email address for an existing account.");
        if(ModelState.IsValid)
        {
            try {await api.SetMember(session,organizationId,form,token);TempData["MemberNotice"]="Membership saved. Roles in other organizations are unchanged.";return Redirect($"/admin/{organizationId}/members");}
            catch(WebApiException ex) when(ex.Status is 400 or 404 or 409)
            {ModelState.AddModelError("",ex.SafeMessage ?? "Account unavailable or membership conflicts with current state.");Response.StatusCode=ex.Status;}
            catch(WebApiException ex) when(ex.Status==429)
            {ModelState.AddModelError("","Too many requests. Wait before trying again.");Response.StatusCode=429;}
            catch(Exception ex) when(ex is HttpRequestException or System.Text.Json.JsonException || ex is WebApiException {Status:502} || ex is OperationCanceledException && !token.IsCancellationRequested)
            {TempData["MemberNotice"]="The change outcome is unknown. Review the stored membership before making another change. The request was not retried.";return Redirect($"/admin/{organizationId}/members");}
        }
        else Response.StatusCode=400;
        form.Confirmed=false;ModelState.Remove(nameof(form.Confirmed));
        var filter=new OrganizationFilterForm {Page=form.Page is >=1 and <=10000?form.Page:1,PageSize=form.PageSize is >=1 and <=50?form.PageSize:20,
            Search=form.Search?.Length<=100?form.Search:null,Role=form.FilterRole is >=1 and <=3?form.FilterRole:null};
        return View("Members",(await api.Members(session,organizationId,filter,token)) with {Form=form});
    }
    [HttpGet("courses/{courseId:guid}/instructor")]
    public async Task<IActionResult> Instructor(Guid organizationId,Guid courseId,CancellationToken token,int page=1,string? search=null)
        =>View(await api.InstructorChoices(Session(organizationId),organizationId,courseId,token,new(){Page=page,PageSize=50,Search=search}));
    [HttpPost("courses/{courseId:guid}/instructor")]
    public async Task<IActionResult> Instructor(Guid organizationId,Guid courseId,Guid instructorId,CancellationToken token)
    {
        try
        {
            await api.AssignInstructor(Session(organizationId),organizationId,courseId,instructorId,token);
            return Redirect($"/staff/{organizationId}/courses/{courseId}");
        }
        catch(WebApiException ex) when(ex.Status is 400 or 409)
        {
            ModelState.AddModelError("","Choose an existing Instructor member of this organization.");Response.StatusCode=ex.Status;
            return View(await api.InstructorChoices(Session(organizationId),organizationId,courseId,token));
        }
    }
}
