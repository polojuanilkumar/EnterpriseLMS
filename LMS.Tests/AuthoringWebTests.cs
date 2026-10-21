using System.Text.Json;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LMS.Tests;

public sealed class AuthoringWebTests
{
    private static LearnerWebTests.Fixture Setup(string? role="Instructor",bool platform=false)
    {
        var f=new LearnerWebTests.Fixture();f.Handler.Role=role;f.Handler.Platform=platform;
        f.Handler.Responses["/api/Categories"]=new[]{new CategoryChoice(Guid.NewGuid(),"Category",true)};
        f.Handler.Responses[$"/api/Courses/{f.Course}"]=new CourseDetail(f.Course,"Course","CODE","Description",null,f.Session.UserId,"Advanced","Draft",25,"https://example.test/image");
        f.Handler.Responses[$"/api/Courses/{f.Course}/sections/{f.Section}"]=new SectionDetail(f.Section,f.Course,"Section","Description",2);
        f.Handler.Responses[$"/api/Lessons/{f.Lesson}"]=new LessonDetail(f.Lesson,f.Section,"Lesson","Description","Video","legacy text","https://youtu.be/dQw4w9WgXcQ",12,3);
        return f;
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Create_and_edit_use_existing_routes_and_selected_scope(string? role,bool platform)
    {
        using var f=Setup(role,platform);
        var form=new CourseForm{Title="New",Code="NEW",Description="Body",InstructorId=f.Session.UserId,Level=2,DurationInMinutes=15};
        await f.Api.SaveCourse(f.Session,f.Org,null,form,default);
        Assert.Equal("/api/Courses",f.Handler.Requests.Last().Path);Assert.Equal("POST",f.Handler.Requests.Last().Method);
        await f.Api.SaveCourse(f.Session,f.Org,f.Course,form,default);Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        await f.Api.SaveSection(f.Session,f.Org,f.Course,null,new(){Title="New",DisplayOrder=2},default);
        Assert.Equal($"/api/Courses/{f.Course}/sections",f.Handler.Requests.Last().Path);
        await f.Api.SaveSection(f.Session,f.Org,f.Course,f.Section,new(){Title="Edit",DisplayOrder=3},default);
        Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        await f.Api.SaveLesson(f.Session,f.Org,f.Course,f.Section,null,new(){Title="Lesson",Content="Text"},default);
        Assert.Equal($"/api/Sections/{f.Section}/lessons",f.Handler.Requests.Last().Path);
        var page=await f.Api.LessonEditor(f.Session,f.Org,f.Course,f.Section,f.Lesson,default);
        Assert.Equal(2,page.Form.LessonType);Assert.Equal("legacy text",page.Form.Content);
        page.Form.Title="Edited";
        await f.Api.SaveLesson(f.Session,f.Org,f.Course,f.Section,f.Lesson,page.Form,default);
        var body=JsonDocument.Parse(f.Handler.Bodies.Last()).RootElement;
        Assert.Equal("legacy text",body.GetProperty("content").GetString());Assert.Equal("https://youtu.be/dQw4w9WgXcQ",body.GetProperty("videoUrl").GetString());
        Assert.All(f.Handler.Requests,x=>Assert.Equal(f.Org.ToString(),x.Organization));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("content-items")||x.Path.Contains("publish"));
    }
    [Fact]
    public async Task Instructor_creation_ignores_forged_assignment_and_admin_rejects_nonmember()
    {
        using var f=Setup();var form=new CourseForm{Title="New",InstructorId=Guid.NewGuid()};
        await f.Api.SaveCourse(f.Session,f.Org,null,form,default);
        Assert.Equal(f.Session.UserId,JsonDocument.Parse(f.Handler.Bodies.Last()).RootElement.GetProperty("instructorId").GetGuid());
        f.Handler.Role="Admin";form.InstructorId=Guid.NewGuid();f.Handler.Requests.Clear();
        await Assert.ThrowsAsync<AuthoringValidationException>(()=>f.Api.SaveCourse(f.Session,f.Org,null,form,default));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Method!="GET");
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Staff_cannot_use_foreign_or_unselected_organization(string? role,bool platform)
    {
        using var f=Setup(role,platform);
        Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SaveCourse(f.Session,Guid.NewGuid(),null,new(),default))).Status);
        var unselected=new LearnerSession{AccessToken=f.Session.AccessToken,UserId=f.Session.UserId,ExpiresAt=f.Session.ExpiresAt,IsSuperAdmin=true};
        Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Api.CourseEditor(unselected,f.Org,null,default))).Status);
        Assert.Empty(f.Handler.Requests);
    }
    [Fact]
    public async Task Ownership_and_nested_parent_checks_prevent_writes_and_readiness_reads()
    {
        using var f=Setup();f.Handler.Responses.Remove($"/api/Courses/{f.Course}");f.Handler.OwnsCourse=false;
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SaveSection(f.Session,f.Org,f.Course,null,new(),default));
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.Readiness(f.Session,f.Org,f.Course,default));
        f.Handler.OwnsCourse=true;
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SaveLesson(f.Session,f.Org,f.Course,Guid.NewGuid(),null,new(),default));
        f.Handler.Responses[$"/api/Lessons/{f.Lesson}"]=new LessonDetail(f.Lesson,Guid.NewGuid(),"Bad",null,"Text",null,null,0,1);
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SaveLesson(f.Session,f.Org,f.Course,f.Section,f.Lesson,new(),default));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Method!="GET");
    }
    [Fact]
    public async Task Validation_errors_keep_form_values_and_no_redirect()
    {
        using var f=Setup();f.Handler.ValidationPath="/api/Courses";
        var controller=new AuthoringController(f.Api,f.Sessions){ControllerContext=new(){HttpContext=f.Http()},TempData=new TempDataDictionary(f.Http(),Moq.Mock.Of<ITempDataProvider>())};
        var form=new CourseForm{Title="<script>entered</script>",Code="C",Description="Body"};
        var result=Assert.IsType<ViewResult>(await controller.SaveCourse(f.Org,null,form,default));
        Assert.Same(form,Assert.IsType<CourseEditor>(result.Model).Form);Assert.Equal(400,controller.Response.StatusCode);
        Assert.Contains(controller.ModelState.Values.SelectMany(x=>x.Errors),x=>x.ErrorMessage.Contains("Title is invalid"));
        controller.ModelState.AddModelError("Form.Code","Required");f.Handler.Requests.Clear();
        await controller.SaveCourse(f.Org,null,form,default);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Method!="GET");
    }
    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)]
    public async Task Readiness_displays_legacy_items_and_quiz_state_without_invented_blockers(bool items,bool quiz)
    {
        using var f=Setup();f.Handler.EmptyItems=!items;f.Handler.Legacy=new("Legacy body",null);
        if(quiz)f.Handler.Responses[$"/{f.Lesson}/quiz"]=new QuizState(Guid.NewGuid(),f.Lesson,"Quiz",false);
        var page=await f.Api.Readiness(f.Session,f.Org,f.Course,default);
        var row=Assert.Single(Assert.Single(page.Sections).Lessons);
        Assert.Equal(quiz,row.Quiz is not null);Assert.Equal(items, row.Unit.Content.Items.Length>0);
        Assert.Equal(items?null:"Legacy body",row.Unit.Content.Legacy?.Content);
        var html=await Render("Readiness",page);
        Assert.Contains("No content-based blockers",html);Assert.Contains("not a publication blocker",html);
        Assert.Contains(quiz?"Draft":"None (optional)",html);
        if(!items)Assert.Contains("Legacy lesson content is available",html);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Method!="GET");
    }
    [Fact]
    public async Task Editors_render_encoded_values_and_csrf_without_tokens()
    {
        using var f=Setup();var course=await f.Api.CourseEditor(f.Session,f.Org,null,default);course.Form.Title="<script>bad</script>";
        var section=new SectionEditor(f.Org,f.Course,null,new(){Title="<script>bad</script>"});
        var lesson=new LessonEditor(f.Org,f.Course,f.Section,null,new(){Title="<script>bad</script>",Content="</textarea><script>bad</script>"});
        foreach(var html in new[]{await Render("Course",course),await Render("Section",section),await Render("Lesson",lesson)})
        {Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain(f.Session.AccessToken,html);}
        Assert.True(Attribute.IsDefined(typeof(AuthoringController),typeof(AutoValidateAntiforgeryTokenAttribute)));
    }
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task Section_and_lesson_forms_redirect_on_success_and_retain_api_validation_errors(bool lesson,bool invalid)
    {
        using var f=Setup();
        if(invalid)f.Handler.ValidationPath=lesson?$"/api/Sections/{f.Section}/lessons":$"/api/Courses/{f.Course}/sections";
        var controller=new AuthoringController(f.Api,f.Sessions){ControllerContext=new(){HttpContext=f.Http()},TempData=new TempDataDictionary(f.Http(),Moq.Mock.Of<ITempDataProvider>())};
        var result=lesson?await controller.SaveLesson(f.Org,f.Course,f.Section,null,new(){Title="Entered",Content="Legacy"},default)
            :await controller.SaveSection(f.Org,f.Course,null,new(){Title="Entered",DisplayOrder=4},default);
        if(!invalid)Assert.IsType<RedirectResult>(result);
        else
        {
            var view=Assert.IsType<ViewResult>(result);Assert.Equal(400,controller.Response.StatusCode);
            Assert.Equal("Entered",lesson?Assert.IsType<LessonEditor>(view.Model).Form.Title:Assert.IsType<SectionEditor>(view.Model).Form.Title);
            Assert.False(controller.ModelState.IsValid);
        }
    }
    [Fact]
    public async Task Student_cannot_enter_new_authoring_or_readiness_paths()
    {
        using var f=Setup("Student");
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.CourseEditor(f.Session,f.Org,null,default));
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.SaveCourse(f.Session,f.Org,null,new(),default));
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.Readiness(f.Session,f.Org,f.Course,default));
        Assert.All(f.Handler.Requests,x=>Assert.EndsWith("/context",x.Path));
    }
    private static async Task<string> Render<T>(string name,T model)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();
        var http=new DefaultHttpContext{RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
        var view=engine.GetView(null,$"/Views/Authoring/{name}.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary<T>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));
        return writer.ToString();
    }
}
