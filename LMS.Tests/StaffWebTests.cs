using System.Security.Claims;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
namespace LMS.Tests;

public sealed class StaffWebTests
{
    [Theory]
    [InlineData("Student",false,"learn")] [InlineData("Instructor",false,"staff")]
    [InlineData("Admin",false,"admin")] [InlineData(null,true,"admin")]
    public async Task Organization_selection_routes_verified_roles_without_student_endpoint(string? role,bool platform,string area)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;f.Handler.Platform=platform;
        ClaimsPrincipal? principal=null;
        f.Auth.Setup(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()))
            .Callback((HttpContext _,string? _,ClaimsPrincipal p,AuthenticationProperties? _)=>principal=p).Returns(Task.CompletedTask);
        var result=Assert.IsType<RedirectResult>(await f.Account().SelectOrganization(f.Org,default));
        Assert.Equal($"/{area}/{f.Org}"+(area=="admin"?"":"/courses"),result.Url);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("Enrollments"));
        var session=f.Sessions.Require(principal!);Assert.Equal(role,session.OrganizationRole);Assert.Equal(platform,session.IsSuperAdmin);
        Assert.DoesNotContain(principal!.Claims,x=>x.Type==ClaimTypes.Role);
    }
    [Fact]
    public async Task Global_claims_and_cached_student_role_cannot_authorize_staff()
    {
        using var f=new LearnerWebTests.Fixture();((ClaimsIdentity)f.User.Identity!).AddClaim(new Claim(ClaimTypes.Role,"SuperAdmin"));
        var error=await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffCourses(f.Session,f.Org,default));Assert.Equal(403,error.Status);
        Assert.Single(f.Handler.Requests);
    }
    [Fact]
    public async Task Instructor_list_filters_other_instructors_on_web_server()
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Instructor";
        f.Handler.Courses=[new(f.Course,"Mine","",false,f.Session.UserId),new(Guid.NewGuid(),"Other","",true,Guid.NewGuid())];
        var page=await f.Api.StaffCourses(f.Session,f.Org,default);Assert.Equal(f.Course,Assert.Single(page.Courses).Id);
    }
    [Theory]
    [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Administration_lists_all_selected_organization_courses(string? role,bool platform)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;f.Handler.Platform=platform;
        f.Handler.Courses=[new(f.Course,"Mine","",false,f.Session.UserId),new(Guid.NewGuid(),"Other","",true,Guid.NewGuid())];
        Assert.Equal(2,(await f.Api.StaffCourses(f.Session,f.Org,default)).Courses.Length);
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData("Student",true)]
    public void Staff_session_cannot_enter_student_only_web_routes(string role,bool platform)
    {
        using var f=new LearnerWebTests.Fixture();
        var id=f.Sessions.Create(new LearnerSession{UserId=f.Session.UserId,AccessToken=f.Session.AccessToken,ExpiresAt=f.Session.ExpiresAt,
            OrganizationId=f.Org,OrganizationRole=role,IsSuperAdmin=platform});
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(LearnerSessions.Claim,id)],"test"));
        Assert.Equal(403,Assert.Throws<WebApiException>(()=>f.Sessions.RequireLearner(principal,f.Org)).Status);
    }
    [Fact]
    public async Task My_workspace_refreshes_a_changed_verified_role_without_redirect_loop()
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Admin";ClaimsPrincipal? principal=null;
        f.Auth.Setup(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()))
            .Callback((HttpContext _,string? _,ClaimsPrincipal p,AuthenticationProperties? _)=>principal=p).Returns(Task.CompletedTask);
        Assert.Equal($"/admin/{f.Org}",Assert.IsType<RedirectResult>(await f.Account().Home(default)).Url);
        Assert.Equal("Admin",f.Sessions.Require(principal!).OrganizationRole);
    }
    [Theory]
    [InlineData("save")] [InlineData("delete")] [InlineData("reorder")] [InlineData("publish")] [InlineData("read")]
    public async Task Nonowner_instructor_cannot_read_or_mutate_course_content(string operation)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Instructor";f.Handler.OwnsCourse=false;
        var error=await Assert.ThrowsAsync<WebApiException>(()=>operation switch
        {
            "save"=>f.Api.SaveItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,null,new ContentForm{Title="X",Text="X"},default),
            "delete"=>f.Api.DeleteItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,f.Item,default),
            "reorder"=>f.Api.ReorderItems(f.Session,f.Org,f.Course,f.Section,f.Lesson,[f.Item],default),
            "publish"=>f.Api.Publish(f.Session,f.Org,f.Course,f.Section,f.Lesson,"publish",default),
            _=>f.Api.StaffUnit(f.Session,f.Org,f.Course,f.Section,f.Lesson,default)
        });
        Assert.Equal(403,error.Status);Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Theory]
    [InlineData("section")] [InlineData("lesson")] [InlineData("item")]
    public async Task Staff_mutation_rejects_mismatched_nested_route(string parent)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Instructor";
        var error=await Assert.ThrowsAsync<WebApiException>(()=>f.Api.DeleteItem(f.Session,f.Org,f.Course,parent=="section"?Guid.NewGuid():f.Section,
            parent=="lesson"?Guid.NewGuid():f.Lesson,parent=="item"?Guid.NewGuid():f.Item,default));
        Assert.Equal(404,error.Status);Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Every_staff_role_including_platform_needs_explicit_matching_selection(string? role,bool platform)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;f.Handler.Platform=platform;
        var session=new LearnerSession{AccessToken=f.Session.AccessToken,UserId=f.Session.UserId,ExpiresAt=f.Session.ExpiresAt,IsSuperAdmin=platform};
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffCourses(session,f.Org,default));
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.Members(f.Session,Guid.NewGuid(),default));
        Assert.Empty(f.Handler.Requests);
    }
    [Theory]
    [InlineData("Student")] [InlineData("Instructor")]
    public async Task Membership_administration_rejects_nonadmins(string role)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SetMember(f.Session,f.Org,new MemberForm{UserId=Guid.NewGuid(),Role=2},default));
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Fact]
    public async Task Admin_membership_and_instructor_assignment_use_scoped_api_routes()
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Admin";
        await f.Api.SetMember(f.Session,f.Org,new MemberForm{UserId=f.Session.UserId,Role=2,Confirmed=true},default);
        Assert.Equal($"/api/organizations/{f.Org}/members/{f.Session.UserId}",f.Handler.Requests.Last().Path);
        await f.Api.AssignInstructor(f.Session,f.Org,f.Course,f.Session.UserId,default);
        Assert.Equal($"/api/Courses/{f.Course}/instructor",f.Handler.Requests.Last().Path);
        Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        Assert.All(f.Handler.Requests,x=>Assert.Equal(f.Org.ToString(),x.Organization));
        f.Handler.FailurePath=$"/api/Courses/{f.Course}/instructor";f.Handler.Failure=400;
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.AssignInstructor(f.Session,f.Org,f.Course,Guid.NewGuid(),default));
    }
    [Theory]
    [InlineData("Instructor")] [InlineData("Admin")]
    public async Task Content_crud_order_and_publication_call_existing_authorized_endpoints(string role)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;
        await f.Api.SaveItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,null,new ContentForm{Title="Text",Type=1,Text="Body"},default);
        Assert.Equal("POST",f.Handler.Requests.Last().Method);
        await f.Api.SaveItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,f.Item,new ContentForm{Title="YouTube",Type=2,YouTubeUrl="https://youtu.be/dQw4w9WgXcQ"},default);
        Assert.Equal($"/api/Lessons/{f.Lesson}/content-items/{f.Item}",f.Handler.Requests.Last().Path);
        await f.Api.ReorderItems(f.Session,f.Org,f.Course,f.Section,f.Lesson,[f.Item],default);
        Assert.EndsWith("/reorder",f.Handler.Requests.Last().Path);
        await f.Api.Publish(f.Session,f.Org,f.Course,f.Section,f.Lesson,"unpublish",default);
        Assert.Equal("PATCH",f.Handler.Requests.Last().Method);
        await f.Api.DeleteItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,f.Item,default);
        Assert.Equal("DELETE",f.Handler.Requests.Last().Method);
    }
    [Theory]
    [InlineData(3,"clip.mp3","audio/mpeg")] [InlineData(4,"clip.mp4","video/mp4")] [InlineData(5,"deck.pdf","application/pdf")]
    public async Task Upload_uses_private_api_then_attaches_returned_id(int type,string filename,string mime)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Instructor";f.Handler.ItemType=type;
        using var bytes=new MemoryStream([1,2,3]);
        var form=new ContentForm{Title="Media",Type=type,File=new FormFile(bytes,0,3,"File",filename){Headers=new HeaderDictionary(),ContentType=mime}};
        await f.Api.SaveItem(f.Session,f.Org,f.Course,f.Section,f.Lesson,null,form,default);
        Assert.NotNull(form.AssetId);
        var writes=f.Handler.Requests.Where(x=>x.Method=="POST").ToArray();
        Assert.Equal(2,writes.Length);Assert.EndsWith("/media-assets",writes[0].Path);Assert.EndsWith("/content-items",writes[1].Path);
    }
    [Fact]
    public async Task Publication_requires_explicit_confirmation_and_csrf_applies_to_staff_controllers()
    {
        using var f=new LearnerWebTests.Fixture();
        var controller=new StaffController(f.Api,f.Sessions){ControllerContext=new ControllerContext{HttpContext=f.Http()}};
        Assert.IsType<BadRequestObjectResult>(await controller.PublicationConfirmed(f.Org,f.Course,f.Section,f.Lesson,"publish",false,default));
        Assert.Empty(f.Handler.Requests);
        Assert.True(Attribute.IsDefined(typeof(StaffController),typeof(AutoValidateAntiforgeryTokenAttribute)));
        Assert.True(Attribute.IsDefined(typeof(AdminController),typeof(AutoValidateAntiforgeryTokenAttribute)));
    }
    [Fact]
    public async Task Access_denied_is_terminal_html_with_safe_navigation_not_a_redirect_loop()
    {
        using var f=new LearnerWebTests.Fixture();var http=f.Http();
        await new LearnerSecurity(_=>throw new WebApiException(403)).InvokeAsync(http,f.Sessions);
        Assert.Equal(403,http.Response.StatusCode);Assert.False(http.Response.Headers.ContainsKey("Location"));
        var body=System.Text.Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());Assert.Contains("Access denied",body);Assert.Contains("/account/organizations",body);
    }
    [Fact]
    public async Task Staff_edit_view_encodes_content_and_emits_antiforgery_token()
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Instructor";
        var unit=await f.Api.StaffUnit(f.Session,f.Org,f.Course,f.Section,f.Lesson,default);
        var model=new ContentEditPage(unit,null,new ContentForm{Title="<script>title</script>",Text="</textarea><script>attack</script>"});
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();
        var http=new DefaultHttpContext{RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        var view=engine.GetView(null,"/Views/Staff/Edit.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary<ContentEditPage>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));
        var html=writer.ToString();Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain(f.Session.AccessToken,html);
    }
}
