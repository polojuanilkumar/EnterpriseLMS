using System.Reflection;
using System.Text.Json;
using LMS.Api.Controllers;
using LMS.Api.Middleware;
using LMS.Application.Common.Models;
using LMS.Application.Features.Assignments;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;
namespace LMS.Tests;

public sealed class AssignmentApiTests
{
    private static AssignmentsController Controller(AssignmentPolicyTests.Fixture f)
    {
        var h=new DefaultHttpContext();h.Request.ContentType="multipart/form-data; boundary=test";h.Request.Form=new FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>());
        return new(f.Handler,f.Org){ControllerContext=new(){HttpContext=h}};
    }
    [Fact] public void Actual_API_endpoints_have_organization_and_role_boundaries_and_separate_download_routes()
    {
        var type=typeof(AssignmentsController);Assert.True(Attribute.IsDefined(type,typeof(OrganizationScopeAttribute)));Assert.Equal("api/Lessons/{lessonId:guid}/assignments",type.GetCustomAttribute<RouteAttribute>()!.Template);
        foreach(var method in new[]{"Create","Edit","Publish","Submissions","Grade"})Assert.Equal("Instructor,Admin,SuperAdmin",type.GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        foreach(var method in new[]{"Submit","Mine","History"})Assert.Equal("Student",type.GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("{assignmentId:guid}/submissions/{submissionId:guid}/attachment",type.GetMethod("Attachment")!.GetCustomAttribute<HttpGetAttribute>()!.Template);Assert.NotNull(type.GetMethod("Attachment")!.GetCustomAttribute<HttpHeadAttribute>());
        Assert.NotNull(type.GetMethod("Submit")!.GetCustomAttribute<RequestSizeLimitAttribute>());Assert.NotNull(type.GetMethod("Submit")!.GetCustomAttribute<RequestFormLimitsAttribute>());
    }
    [Fact] public async Task API_final_submission_returns_created_owner_snapshot_without_attachment_identifiers()
    {
        var f=new AssignmentPolicyTests.Fixture();var c=Controller(f);
        var result=Assert.IsType<ObjectResult>(await c.Submit(f.Parent.LessonId,f.A.Id,new(){Text="Final",Confirmed=true},default));Assert.Equal(201,result.StatusCode);
        var response=Assert.IsType<ApiResponse<SubmissionResponse>>(result.Value);Assert.True(response.Success);Assert.Equal(f.Org.UserId,response.Data!.UserId);Assert.Equal("Final",response.Data.Text);Assert.Equal(f.Clock.Now,response.Data.SubmittedAt);
        var json=JsonSerializer.Serialize(response);Assert.DoesNotContain("AttachmentId",json);Assert.DoesNotContain("StoragePath",json);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Submit(f.Parent.LessonId,f.A.Id,new(){Text="Replace",Confirmed=true},default));Assert.Single(f.Repo.Work);
    }
    [Fact] public async Task API_rejects_multiple_files_before_storage_or_persistence()
    {
        var f=new AssignmentPolicyTests.Fixture();var c=Controller(f);using var one=AssignmentPolicyTests.Pdf();using var two=AssignmentPolicyTests.Pdf();
        c.Request.Form=new FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>(),new FormFileCollection{new FormFile(one,0,one.Length,"File","one.pdf"),new FormFile(two,0,two.Length,"Other","two.pdf")});
        await Assert.ThrowsAsync<ArgumentException>(()=>c.Submit(f.Parent.LessonId,f.A.Id,new(){Text="Work",Confirmed=true},default));Assert.Null(f.Storage.Last);Assert.Empty(f.Repo.Work);
    }
    [Fact] public async Task API_owner_list_and_history_exclude_other_users_and_GETs_do_not_save()
    {
        var f=new AssignmentPolicyTests.Fixture();var mine=f.Work();f.Work(Guid.NewGuid());var c=Controller(f);
        var result=Assert.IsType<OkObjectResult>(await c.Mine(f.Parent.LessonId,f.A.Id,default));var data=Assert.IsType<ApiResponse<SubmissionResponse[]>>(result.Value).Data!;Assert.Equal(mine.Id,Assert.Single(data).Id);
        var history=Assert.IsType<OkObjectResult>(await c.History(default));Assert.Equal(mine.Id,Assert.Single(Assert.IsType<ApiResponse<AssignmentHistory[]>>(history.Value).Data!).SubmissionId);
        await c.List(f.Parent.LessonId,default);await c.Context(f.Parent.LessonId,default);await c.Detail(f.Parent.LessonId,f.A.Id,default);Assert.Equal(0,f.Repo.SaveCalls);Assert.Equal(0,f.Repo.LockCalls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task API_download_authorizes_owner_or_permitted_staff_and_sets_private_download_headers(bool staff)
    {
        var f=new AssignmentPolicyTests.Fixture(staff?OrganizationRole.Admin:OrganizationRole.Student);var s=new AssignmentSubmission(f.A,f.Org.UserId,"Work",Guid.NewGuid(),"unsafe<name>.pdf",100,f.Clock.Now.UtcDateTime);f.Repo.Work.Add(s);var c=Controller(f);
        var result=Assert.IsType<FileStreamResult>(await c.Attachment(f.Parent.LessonId,f.A.Id,s.Id,default));await using var stream=result.FileStream;
        Assert.Equal("application/pdf",result.ContentType);Assert.Equal($"assignment-{s.Id:N}.pdf",result.FileDownloadName);Assert.Equal("private, no-store",c.Response.Headers.CacheControl);Assert.Equal("nosniff",c.Response.Headers["X-Content-Type-Options"]);Assert.Contains("sandbox",c.Response.Headers["Content-Security-Policy"].ToString());Assert.Equal(1,f.Storage.OpenCalls);
        if(!staff){var other=f.Work(Guid.NewGuid());await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>c.Attachment(f.Parent.LessonId,f.A.Id,other.Id,default));Assert.Equal(1,f.Storage.OpenCalls);}
    }
}
