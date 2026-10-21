using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Controller=LMS.Web.Controllers.AssignmentsController;
namespace LMS.Tests;

public sealed class AssignmentWebTests
{
    private sealed class Fixture:IDisposable
    {
        public LearnerWebTests.Fixture F=new();public Guid Assignment=Guid.NewGuid(),Submission=Guid.NewGuid();public AssignmentView A;public AssignmentSubmissionView S;public AssignmentContextView Context;
        public FaultHandler Handler;private HttpClient client;public LearnerApi Api;
        public Fixture()
        {
            var f=F;Context=new(new(f.Lesson,f.Section,f.Course,f.Org,f.Session.UserId,true,true),1);
            A=new(Assignment,f.Lesson,"<script>Title</script>","<script>Instructions</script>",f.Clock.Now.AddDays(1),10,true,f.Clock.Now,null,false);
            S=new(Submission,Assignment,f.Session.UserId,"<script>Work</script>",true,"<img onerror=bad>.pdf",f.Clock.Now,A.Instructions,10,null);
            Handler=new(f.Handler);client=new(Handler){BaseAddress=new("https://api.example.test/")};Api=new(client);Setup([]);
        }
        public string Path=>$"/api/Lessons/{F.Lesson}/assignments";
        public void Setup(AssignmentSubmissionView[] work)
        {
            F.Handler.Responses[Path+"/context"]=Context;F.Handler.Responses[Path]=new[]{A};F.Handler.Responses[Path+"/"+Assignment]=A;
            F.Handler.Responses[Path+$"/{Assignment}/submissions"]=work;F.Handler.Responses[Path+$"/{Assignment}/submissions/me"]=work;
            F.Handler.Responses[Path+$"/{Assignment}/submissions/{Submission}"]=S;
        }
        public Task<AssignmentPage> Page(bool staff=false,Guid? assignment=null,Guid? submission=null)=>Api.AssignmentPage(F.Session,F.Org,F.Course,F.Section,F.Lesson,staff,assignment,submission,default);
        public Controller Controller(bool staff=false)
        {var h=F.Http();h.Request.Path=$"/{(staff?"staff":"learn")}/{F.Org}/courses/{F.Course}/sections/{F.Section}/{(staff?"lessons":"units")}/{F.Lesson}/assignments";return new(Api,F.Sessions){ControllerContext=new(){HttpContext=h},TempData=new TempDataDictionary(h,Moq.Mock.Of<ITempDataProvider>())};}
        public void Staff(string? role="Instructor",bool platform=false){F.Handler.Role=role;F.Handler.Platform=platform;}
        public void Dispose(){client.Dispose();F.Dispose();}
    }
    private sealed class FaultHandler(HttpMessageHandler inner):HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker=new(inner,false);public string? Fault;public int Mutations;public string? Body;public byte[]? Attachment;public string? DownloadPath;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method!=HttpMethod.Get)
            {
                Mutations++;Body=request.Content is null?null:await request.Content.ReadAsStringAsync(token);
                if(Fault=="network")throw new HttpRequestException("PRIVATE secret diagnostics");
                if(Fault=="timeout")throw new TaskCanceledException("PRIVATE secret diagnostics");
                if(Fault=="gateway")return new(HttpStatusCode.BadGateway);
                if(Fault=="conflict")return new(HttpStatusCode.Conflict){Content=JsonContent.Create(new {message="Stored state conflict <script>unsafe</script>"})};
            }
            if(request.RequestUri!.AbsolutePath.EndsWith("/attachment")&&Attachment is not null)
            {DownloadPath=request.RequestUri.AbsolutePath;var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Attachment)};r.Content.Headers.ContentType=new("application/pdf");r.Headers.Add("Set-Cookie","must-not-forward=yes");return r;}
            return await invoker.SendAsync(request,token);
        }
    }
    [Theory][InlineData("Instructor",false)][InlineData("Admin",false)][InlineData(null,true)]
    public async Task Authoring_uses_exact_routes_server_tokens_selected_org_and_UTC_input(string? role,bool platform)
    {
        using var f=new Fixture();f.Staff(role,platform);var page=await f.Page(true,f.Assignment);
        await f.Api.SaveAssignment(f.F.Session,page,new(){Title="Safe title",Instructions="Plain text",DueAtUtc=new DateTime(2026,10,10,12,30,0,DateTimeKind.Unspecified),MaximumMarks=10},default);
        var sent=f.F.Handler.Requests.Last();Assert.Equal("PUT",sent.Method);Assert.Equal(f.Path+"/"+f.Assignment,sent.Path);Assert.Equal(f.F.Org.ToString(),sent.Organization);Assert.Equal("Bearer "+f.F.Session.AccessToken,sent.Authorization);
        using var body=JsonDocument.Parse(f.Handler.Body!);Assert.Equal(TimeSpan.Zero,body.RootElement.GetProperty("dueAtUtc").GetDateTimeOffset().Offset);Assert.Equal(12,body.RootElement.GetProperty("dueAtUtc").GetDateTimeOffset().Hour);
        await f.Api.SaveAssignment(f.F.Session,await f.Page(true),new(){Title="New",Instructions="Work",MaximumMarks=1},default);Assert.Equal("POST",f.F.Handler.Requests.Last().Method);Assert.Equal(f.Path,f.F.Handler.Requests.Last().Path);
    }
    [Fact] public async Task Authoring_ownership_nested_parents_org_and_explicit_selection_prevent_mutations()
    {
        using var f=new Fixture();f.Staff();f.Context=f.Context with {Parent=f.Context.Parent with {InstructorId=Guid.NewGuid()}};f.Setup([]);
        await Assert.ThrowsAsync<WebApiException>(()=>f.Page(true));Assert.Equal(0,f.Handler.Mutations);
        f.Context=f.Context with {Parent=f.Context.Parent with {InstructorId=f.F.Session.UserId,SectionId=Guid.NewGuid()}};f.Setup([]);
        Assert.Equal(404,(await Assert.ThrowsAsync<WebApiException>(()=>f.Page(true))).Status);
        f.Context=f.Context with {Parent=f.Context.Parent with {SectionId=f.F.Section,OrganizationId=Guid.NewGuid()}};f.Setup([]);
        Assert.Equal(404,(await Assert.ThrowsAsync<WebApiException>(()=>f.Page(true))).Status);
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.AssignmentPage(new LearnerSession{UserId=f.F.Session.UserId,AccessToken=f.F.Session.AccessToken,ExpiresAt=f.F.Session.ExpiresAt},f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,true,null,null,default));
        Assert.Equal(0,f.Handler.Mutations);
    }
    [Fact] public async Task Publication_needs_confirmation_and_backend_validation_retains_safe_form_values()
    {
        using var f=new Fixture();f.Staff();var c=f.Controller(true);var org=f.F.Org;
        var denied=Assert.IsType<ViewResult>(await c.Publish(org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,false,false,default));Assert.Equal("Detail",denied.ViewName);Assert.Equal(0,f.Handler.Mutations);Assert.False(c.ModelState.IsValid);
        c=f.Controller(true);var form=new AssignmentForm{Title="<script>Retained</script>",Instructions="<script>Work</script>",MaximumMarks=10};f.F.Handler.ValidationPath=f.Path+"/"+f.Assignment;
        var result=Assert.IsType<ViewResult>(await c.Save(org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,form,default));Assert.Same(form,Assert.IsType<AssignmentPage>(result.Model).Form);Assert.False(c.ModelState.IsValid);
        var html=await Render("Edit",result.Model!,c.ViewData.ModelState);Assert.Contains("&lt;script&gt;Retained",html);Assert.DoesNotContain("<script>Retained",html);
        Assert.Contains("Title is invalid &lt;script&gt;bad",html);
    }
    [Fact] public async Task Confirmed_publication_and_save_use_PRG_and_never_mutate_on_GET()
    {
        using var f=new Fixture();f.Staff();var c=f.Controller(true);
        Assert.IsType<ViewResult>(await c.Index(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,default));Assert.Equal(0,f.Handler.Mutations);
        Assert.IsType<RedirectResult>(await c.Publish(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,false,true,default));
        Assert.Equal("PATCH",f.F.Handler.Requests.Last().Method);Assert.Equal(f.Path+$"/{f.Assignment}/publication",f.F.Handler.Requests.Last().Path);
        Assert.Contains("\"confirmed\":true",f.Handler.Body!);
    }
    [Fact] public async Task Frozen_title_edit_restores_protected_fields_from_stored_values_including_exact_due_time()
    {
        using var f=new Fixture();f.Staff();f.A=f.A with {Frozen=true,DueAtUtc=f.F.Clock.Now.AddTicks(1234567),Instructions="Line one\nLine two"};f.Setup([f.S]);var c=f.Controller(true);
        c.ModelState.AddModelError("Form.Instructions","Missing disabled field");
        Assert.IsType<RedirectResult>(await c.Save(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,new(){Title="New title",Instructions="Tampered",MaximumMarks=1,DueAtUtc=null},default));
        using var body=JsonDocument.Parse(f.Handler.Body!);Assert.Equal(f.A.Instructions,body.RootElement.GetProperty("instructions").GetString());Assert.Equal(f.A.MaximumMarks,body.RootElement.GetProperty("maximumMarks").GetDecimal());Assert.Equal(f.A.DueAtUtc,body.RootElement.GetProperty("dueAtUtc").GetDateTimeOffset());
    }
    [Fact] public async Task Final_submission_payload_is_multipart_with_text_PDF_confirmation_and_no_token_in_body()
    {
        using var f=new Fixture();var page=await f.Page(false,f.Assignment);using var pdf=AssignmentPolicyTests.Pdf();
        var form=new AssignmentSubmissionForm{Text="<script>Submitted</script>",Confirmed=true,File=new FormFile(pdf,0,pdf.Length,"File","work.pdf"){Headers=new HeaderDictionary(),ContentType="application/pdf"}};
        var c=f.Controller();Assert.IsType<RedirectResult>(await c.Submit(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,form,default));
        Assert.Equal(1,f.Handler.Mutations);Assert.Contains("name=Text",f.Handler.Body!);Assert.Contains("<script>Submitted",f.Handler.Body!);Assert.Contains("application/pdf",f.Handler.Body!);Assert.Contains("%PDF-1.7",f.Handler.Body!);Assert.DoesNotContain(f.F.Session.AccessToken,f.Handler.Body!);
        Assert.Equal(f.Path+$"/{f.Assignment}/submissions",f.F.Handler.Requests.Last().Path);
    }
    [Theory][InlineData("network")][InlineData("gateway")][InlineData("timeout")]
    public async Task Ambiguous_submission_failures_render_read_only_recovery_without_replay(string fault)
    {
        using var f=new Fixture();f.Handler.Fault=fault;var c=f.Controller();
        var result=Assert.IsType<ViewResult>(await c.Submit(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,new(){Text="Work",Confirmed=true},default));
        Assert.Equal("Recovery",result.ViewName);Assert.Equal(1,f.Handler.Mutations);Assert.Equal(502,c.Response.StatusCode);
        var html=await Render("Recovery",result.Model!);Assert.Contains("outcome is unknown",html);Assert.DoesNotContain("<form",html);Assert.DoesNotContain("PRIVATE",html);Assert.DoesNotContain(f.F.Session.AccessToken,html);
    }
    [Fact] public async Task Duplicate_conflict_is_clear_safe_and_does_not_resend_submission()
    {
        using var f=new Fixture();f.Handler.Fault="conflict";var c=f.Controller();
        var form=new AssignmentSubmissionForm{Text="<script>Retained</script>",Confirmed=true};
        var result=Assert.IsType<ViewResult>(await c.Submit(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,form,default));
        Assert.Equal(1,f.Handler.Mutations);Assert.Same(form,Assert.IsType<AssignmentPage>(result.Model).SubmissionForm);
        var html=await Render("Detail",result.Model!,c.ModelState);Assert.Contains("Stored state conflict &lt;script&gt;unsafe",html);Assert.Contains("&lt;script&gt;Retained",html);
    }
    [Fact] public async Task Submission_confirmation_required_and_completed_or_existing_work_offer_no_new_submission()
    {
        using var f=new Fixture();var c=f.Controller();Assert.IsType<ViewResult>(await c.Submit(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,new(){Text="Work"},default));Assert.Equal(0,f.Handler.Mutations);
        f.Context=f.Context with {EnrollmentStatus=2};f.Setup([]);var html=await Render("Detail",await f.Page(false,f.Assignment));Assert.DoesNotContain("Submit final work",html);
        f.Context=f.Context with {EnrollmentStatus=1};f.Setup([f.S]);html=await Render("Detail",await f.Page(false,f.Assignment));Assert.DoesNotContain("Submit final work",html);Assert.Contains("Your submission",html);
    }
    [Fact] public async Task Owner_history_review_works_for_unpublished_completed_but_other_student_is_rejected()
    {
        using var f=new Fixture();f.Context=f.Context with {EnrollmentStatus=2,Parent=f.Context.Parent with {CoursePublished=false,LessonPublished=false}};f.A=f.A with {IsPublished=false};f.Setup([f.S]);
        var page=await f.Page(false,f.Assignment,f.Submission);Assert.NotNull(page.Submission);Assert.Equal(0,f.Handler.Mutations);
        Assert.DoesNotContain(f.F.Handler.Requests,r=>r.Path.Contains("/content-items")||r.Path.Contains("/progress"));
        f.S=f.S with {UserId=Guid.NewGuid()};f.Setup([f.S]);Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Page(false,f.Assignment,f.Submission))).Status);
        f.Context=f.Context with {EnrollmentStatus=3};f.Setup([]);Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Page())).Status);
    }
    [Fact] public async Task Grading_confirmation_PRG_repeat_conflict_and_ambiguous_failure_are_safe()
    {
        using var f=new Fixture();f.Staff();f.Setup([f.S]);var c=f.Controller(true);
        Assert.IsType<ViewResult>(await c.Grade(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,f.Submission,new(){Marks=5},default));Assert.Equal(0,f.Handler.Mutations);
        c=f.Controller(true);Assert.IsType<RedirectResult>(await c.Grade(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,f.Submission,new(){Marks=5,Feedback="Feedback",Confirmed=true},default));Assert.Equal(1,f.Handler.Mutations);Assert.Equal(f.Path+$"/{f.Assignment}/submissions/{f.Submission}/grade",f.F.Handler.Requests.Last().Path);
        f.Handler.Fault="conflict";c=f.Controller(true);var conflict=Assert.IsType<ViewResult>(await c.Grade(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,f.Submission,new(){Marks=2,Feedback="Retain",Confirmed=true},default));Assert.Equal("Review",conflict.ViewName);Assert.Equal("Retain",Assert.IsType<AssignmentPage>(conflict.Model).GradeForm.Feedback);
        f.Handler.Fault="network";c=f.Controller(true);var uncertain=Assert.IsType<ViewResult>(await c.Grade(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,f.Submission,new(){Marks=1,Confirmed=true},default));Assert.Equal("Recovery",uncertain.ViewName);Assert.Equal(3,f.Handler.Mutations);
    }
    [Fact] public async Task All_forms_encode_text_include_antiforgery_and_grade_is_not_offered_after_grading()
    {
        using var f=new Fixture();f.Staff();var editor=await Render("Edit",await f.Page(true,f.Assignment));Assert.Contains("UTC, optional",editor);Assert.Contains("__RequestVerificationToken",editor);Assert.Contains("&lt;script&gt;Instructions",editor);
        var detail=await Render("Detail",await f.Page(true,f.Assignment));Assert.Contains("Confirm publication",detail.Replace("Confirm unpublishing (new submissions will be blocked)","Confirm publication"));Assert.Contains("__RequestVerificationToken",detail);
        f.Staff("Student");var submit=await Render("Detail",await f.Page(false,f.Assignment));Assert.Contains("__RequestVerificationToken",submit);Assert.Contains("my final submission",submit);
        f.Staff();f.Setup([f.S]);var review=await Render("Review",await f.Page(true,f.Assignment,f.Submission));Assert.Contains("__RequestVerificationToken",review);Assert.Contains("&lt;script&gt;Work",review);Assert.Contains("&lt;img onerror=bad&gt;.pdf",review);
        f.S=f.S with {Grade=new(5,"<script>Feedback</script>",Guid.NewGuid(),f.F.Clock.Now)};f.Setup([f.S]);var graded=await Render("Review",await f.Page(true,f.Assignment,f.Submission));Assert.Contains("&lt;script&gt;Feedback",graded);Assert.DoesNotContain("Save final grade",graded);
        foreach(var html in new[]{editor,detail,submit,review,graded}){Assert.DoesNotContain("<script>Title",html);Assert.DoesNotContain(f.F.Session.AccessToken,html);Assert.DoesNotContain("AttachmentId",html);}
        Assert.True(Attribute.IsDefined(typeof(Controller),typeof(AutoValidateAntiforgeryTokenAttribute)));
    }
    [Fact] public async Task Assignment_forms_antiforgery_rejects_missing_and_accepts_cookie_bound_token()
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var csrf=scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var get=new DefaultHttpContext{RequestServices=scope.ServiceProvider};get.Request.Scheme="https";var tokens=csrf.GetAndStoreTokens(get);
        var post=new DefaultHttpContext{RequestServices=scope.ServiceProvider};post.Request.Method="POST";post.Request.Scheme="https";
        await Assert.ThrowsAsync<AntiforgeryValidationException>(()=>csrf.ValidateRequestAsync(post));post.Request.Headers.Cookie=get.Response.Headers.SetCookie.ToString().Split(';')[0];post.Request.Headers[tokens.HeaderName!]=tokens.RequestToken;await csrf.ValidateRequestAsync(post);
    }
    [Fact] public void Actual_MVC_routes_include_one_absolute_history_route_and_protect_every_POST()
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();
        var routes=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>().Where(x=>x.ControllerTypeInfo.AsType()==typeof(Controller)).ToArray();
        var history=Assert.Single(routes,x=>x.ActionName=="History");Assert.Equal("learn/{organizationId:guid}/assignments/history",history.AttributeRouteInfo!.Template);
        Assert.Contains(routes,x=>x.AttributeRouteInfo?.Template?.EndsWith("/assignments/new")==true&&x.AttributeRouteInfo.Template.StartsWith("staff/"));
        var postRoutes=routes.Where(x=>x.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().Any(c=>c.HttpMethods.Contains("POST"))==true).ToArray();Assert.NotEmpty(postRoutes);
        Assert.All(postRoutes,x=>Assert.Contains(x.FilterDescriptors,f=>f.Filter is AutoValidateAntiforgeryTokenAttribute));
    }
    [Fact] public async Task Private_download_proxies_only_authorized_work_with_safe_headers_and_generated_name()
    {
        using var f=new Fixture();f.Setup([f.S]);f.Handler.Attachment="%PDF-1.7\ncontent\n%%EOF\n"u8.ToArray();var c=f.Controller();
        await c.Attachment(f.F.Org,f.F.Course,f.F.Section,f.F.Lesson,f.Assignment,f.Submission,default);
        Assert.Equal("private, no-store",c.Response.Headers.CacheControl);Assert.Equal("nosniff",c.Response.Headers["X-Content-Type-Options"]);Assert.StartsWith("attachment; filename=\"assignment-",c.Response.Headers.ContentDisposition.ToString());Assert.Equal("application/pdf",c.Response.ContentType);Assert.Equal(0,c.Response.Headers.SetCookie.Count);
        Assert.Equal(f.Handler.Attachment,((MemoryStream)c.Response.Body).ToArray());Assert.Equal(f.Path+$"/{f.Assignment}/submissions/{f.Submission}/attachment",f.Handler.DownloadPath);
    }
    private static async Task<string> Render(string name,object model,ModelStateDictionary? state=null)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var h=new DefaultHttpContext{RequestServices=scope.ServiceProvider};h.Request.Scheme="https";
        var action=new ActionContext(h,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var view=engine.GetView(null,$"/Views/Assignments/{name}.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),state??new()){Model=model};
        await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(h,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
