using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using LMS.Api.Middleware;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
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
using Reports=LMS.Web.Controllers.ReportsController;
using Gradebook=LMS.Web.Controllers.GradebookController;
namespace LMS.Tests;
public sealed class ReportingWebTests
{
    private sealed class Fixture:IDisposable
    {
        internal LearnerWebTests.Fixture F=new();internal LearnerApi Api;private HttpClient client;internal Capture Handler;internal GradeCourseView Course;internal ReportCourseView StaffCourse;internal QuizGradeView[] QuizRows;internal AssignmentGradebookView[] AssignmentRows;
        internal Fixture()
        {
            Handler=new(F.Handler);client=new(Handler){BaseAddress=new("https://api.example.test/")};Api=new(client);
            Course=new(F.Course,F.Org,F.Session.UserId,"<script>Course</script>",true,2,F.Clock.Now.UtcDateTime,100,2,1,50);
            StaffCourse=new(F.Course,F.Org,F.Session.UserId,Course.Title,true,2,1,1,0,2,1,2,1,1,1,0,3,2,1,1);
            QuizRows=[Quiz(2,0,false,true),Quiz(3,null,null,false),Quiz(2,null,null,false),Quiz(1,null,null,false),Quiz(0,null,null,false)];
            AssignmentRows=[Assignment(2,0),Assignment(1,null),Assignment(0,null)];Setup();
        }
        internal QuizGradeView Quiz(int status,decimal? marks,bool? passed,bool result)=>new(F.Course,F.Section,F.Lesson,Guid.NewGuid(),"<script>Quiz</script>",status==0?null:Guid.NewGuid(),status==0?null:1,status,status==0?null:F.Clock.Now.UtcDateTime,status==2?F.Clock.Now.UtcDateTime:null,F.Clock.Now.UtcDateTime,marks,status==0?null:10,status==0?null:70,passed,status==1,true,result);
        internal AssignmentGradebookView Assignment(int status,decimal? marks)=>new(F.Course,F.Section,F.Lesson,Guid.NewGuid(),"<script>Assignment</script>",status==0?null:Guid.NewGuid(),status==0?null:F.Clock.Now.UtcDateTime,status==2?F.Clock.Now.UtcDateTime:null,10,marks,status==2?"<script>Feedback</script>":null,status,true);
        internal void Setup()
        {
            F.Handler.Responses["/api/Reports/courses"]=new ReportPageView<ReportCourseView>(F.Org,null,1,20,1,[StaffCourse]);
            F.Handler.Responses[$"/api/Reports/courses/{F.Course}/learners"]=new CourseLearnerReportView(new(F.Course,F.Org,F.Session.UserId,StaffCourse.Title,true),new(F.Org,null,1,20,1,[new(F.Course,Guid.NewGuid(),"<script>Name</script>","Learner",2,F.Clock.Now.UtcDateTime,F.Clock.Now.UtcDateTime,100,2,1,50,false,1,2,1,1,1,0,2,1,1)]));
            F.Handler.Responses["/api/Gradebook/courses"]=new ReportPageView<GradeCourseView>(F.Org,F.Session.UserId,1,20,1,[Course]);F.Handler.Responses[$"/api/Gradebook/courses/{F.Course}"]=Course;
            F.Handler.Responses[$"/api/Gradebook/courses/{F.Course}/quizzes"]=new ReportPageView<QuizGradeView>(F.Org,F.Session.UserId,1,20,QuizRows.Length,QuizRows);
            F.Handler.Responses[$"/api/Gradebook/courses/{F.Course}/assignments"]=new ReportPageView<AssignmentGradebookView>(F.Org,F.Session.UserId,1,20,AssignmentRows.Length,AssignmentRows);
        }
        internal Reports Staff(){var h=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=h},TempData=new TempDataDictionary(h,Mock.Of<ITempDataProvider>())};}
        internal Gradebook Learner(){var h=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=h},TempData=new TempDataDictionary(h,Mock.Of<ITempDataProvider>())};}
        public void Dispose(){client.Dispose();F.Dispose();}
    }
    private sealed class Capture(HttpMessageHandler inner):HttpMessageHandler
    {private readonly HttpMessageInvoker invoker=new(inner,false);internal List<string> Uris=[];protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken t){Uris.Add(request.RequestUri!.PathAndQuery);return invoker.SendAsync(request,t);}}
    [Theory][InlineData("Instructor",false)][InlineData("Admin",false)][InlineData(null,true)]
    public async Task Staff_pages_use_selected_org_roles_read_only_report_endpoints(string? role,bool platform)
    {
        using var f=new Fixture();f.F.Handler.Role=role;f.F.Handler.Platform=platform;var c=f.Staff();
        var result=Assert.IsType<ViewResult>(await c.Index(f.F.Org,new(),default));Assert.IsType<StaffReportsPage>(result.Model);await c.Learners(f.F.Org,f.F.Course,new(),default);
        Assert.All(f.F.Handler.Requests,x=>{Assert.Equal("GET",x.Method);Assert.Equal(f.F.Org.ToString(),x.Organization);Assert.Equal("Bearer "+f.F.Session.AccessToken,x.Authorization);});
        Assert.Contains(f.Handler.Uris,x=>x.StartsWith("/api/Reports/courses?page=1&pageSize=20"));Assert.DoesNotContain(f.Handler.Uris,x=>x.Contains("content-items")||x.Contains("/answers"));
    }
    [Fact] public async Task Reports_reject_foreign_org_unassigned_course_and_cross_parent_results()
    {
        using var f=new Fixture();f.F.Handler.Role="Instructor";f.StaffCourse=f.StaffCourse with {InstructorId=Guid.NewGuid()};f.Setup();Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffReports(f.F.Session,f.F.Org,new(),default))).Status);
        f.StaffCourse=f.StaffCourse with {InstructorId=f.F.Session.UserId,OrganizationId=Guid.NewGuid()};f.Setup();await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffReports(f.F.Session,f.F.Org,new(),default));
        var wrong=new CourseLearnerReportView(new(Guid.NewGuid(),f.F.Org,f.F.Session.UserId,"Wrong",true),new(f.F.Org,null,1,20,0,[]));f.F.Handler.Responses[$"/api/Reports/courses/{f.F.Course}/learners"]=wrong;Assert.Equal(404,(await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffReportLearners(f.F.Session,f.F.Org,f.F.Course,new(),default))).Status);
    }
    [Fact] public async Task Gradebook_refuses_other_owner_cancelled_course_foreign_totals_and_invalid_pages()
    {
        using var f=new Fixture();f.Course=f.Course with {UserId=Guid.NewGuid()};f.Setup();await Assert.ThrowsAsync<WebApiException>(()=>f.Api.MyGrades(f.F.Session,f.F.Org,new(),default));
        f.Course=f.Course with {UserId=f.F.Session.UserId,EnrollmentStatus=3};f.Setup();await Assert.ThrowsAsync<WebApiException>(()=>f.Api.CourseGrades(f.F.Session,f.F.Org,f.F.Course,1,1,20,null,null,default));
        f.F.Handler.Responses["/api/Gradebook/courses"]=new ReportPageView<GradeCourseView>(Guid.NewGuid(),f.F.Session.UserId,1,20,1000,[]);await Assert.ThrowsAsync<WebApiException>(()=>f.Api.MyGrades(f.F.Session,f.F.Org,new(),default));
        Assert.Equal(400,(await Assert.ThrowsAsync<WebApiException>(()=>f.Api.MyGrades(f.F.Session,f.F.Org,new(){PageSize=51},default))).Status);
    }
    [Fact] public async Task Course_gradebook_has_fixed_number_of_GETs_and_no_per_assessment_calls()
    {
        using var f=new Fixture();var result=Assert.IsType<ViewResult>(await f.Learner().Course(f.F.Org,f.F.Course));var page=Assert.IsType<CourseGradesPage>(result.Model);Assert.Equal(5,page.Quizzes.Items.Length);Assert.Equal(3,page.Assignments.Items.Length);
        Assert.Equal(4,f.F.Handler.Requests.Count);Assert.All(f.F.Handler.Requests,x=>Assert.Equal("GET",x.Method));Assert.DoesNotContain(f.Handler.Uris,x=>x.Contains("/result")||x.Contains("/submissions")||x.Contains("content-items"));
    }
    [Fact] public async Task Historical_completed_gradebook_keeps_snapshots_and_suppresses_unavailable_quiz_links()
    {
        using var f=new Fixture();f.Course=f.Course with {Published=false};f.QuizRows=f.QuizRows.Select(q=>q with {DetailAvailable=false}).ToArray();f.AssignmentRows=f.AssignmentRows.Where(a=>a.SubmissionId.HasValue).Select(a=>a with {Published=false}).ToArray();f.Setup();
        var page=await f.Api.CourseGrades(f.F.Session,f.F.Org,f.F.Course,1,1,20,null,null,default);var html=await Render("Gradebook/Course",page);
        Assert.Contains("Historical enrollment completed at",html);Assert.Contains("50%",html);Assert.Contains("100%",html);Assert.Contains("Score: 0 / 10",html);Assert.Contains("Grade: 0 / 10",html);Assert.Contains("70%",html);Assert.Contains("Detailed quiz page is unavailable",html);Assert.DoesNotContain("Open existing quiz page",html);Assert.Contains("Open your existing submission review",html);
    }
    [Fact] public async Task Rendering_distinguishes_statuses_encodes_all_names_titles_feedback_and_keeps_tokens_out()
    {
        using var f=new Fixture();var html=await Render("Gradebook/Course",await f.Api.CourseGrades(f.F.Session,f.F.Org,f.F.Course,1,1,20,null,null,default));
        Assert.Contains("Not attempted",html);Assert.Contains("Expired",html);Assert.Contains("result unavailable",html);Assert.Contains("deadline passed",html);Assert.Contains("Not submitted",html);Assert.Contains("Submitted / ungraded",html);Assert.Contains("Grade: 0 / 10",html);Assert.Contains("&lt;script&gt;Feedback",html);Assert.DoesNotContain("<script>Feedback",html);Assert.DoesNotContain(f.F.Session.AccessToken,html);Assert.DoesNotContain("<form method=\"post\"",html);
        f.F.Handler.Role="Admin";var report=await f.Api.StaffReportLearners(f.F.Session,f.F.Org,f.F.Course,new(),default);html=await Render("Reports/Learners",report);Assert.Contains("&lt;script&gt;Name",html);Assert.Contains("Historical enrollment",html);Assert.DoesNotContain("<script>Name",html);Assert.DoesNotContain(f.F.Session.AccessToken,html);
        html=await Render("Reports/Index",await f.Api.StaffReports(f.F.Session,f.F.Org,new(),default));Assert.Contains("&lt;script&gt;Course",html);Assert.Contains("learner-lessons",html);
    }
    [Fact] public async Task Empty_reports_and_gradebook_are_clear_and_keep_existing_navigation()
    {
        using var f=new Fixture();f.F.Handler.Responses["/api/Gradebook/courses"]=new ReportPageView<GradeCourseView>(f.F.Org,f.F.Session.UserId,1,20,0,[]);var html=await Render("Gradebook/Index",await f.Api.MyGrades(f.F.Session,f.F.Org,new(),default));Assert.Contains("No accessible gradebook courses",html);Assert.Contains("My Courses",html);Assert.Contains("Assignment history",html);
        f.F.Handler.Role="Admin";f.F.Handler.Responses["/api/Reports/courses"]=new ReportPageView<ReportCourseView>(f.F.Org,null,1,20,0,[]);html=await Render("Reports/Index",await f.Api.StaffReports(f.F.Session,f.F.Org,new(),default));Assert.Contains("No matching courses",html);Assert.Contains("Courses and authoring",html);
    }
    [Fact] public async Task Invalid_query_binding_sessions_and_forbidden_resources_fail_without_mutations()
    {
        using var f=new Fixture();f.F.Handler.Role="Admin";var c=f.Staff();c.ModelState.AddModelError("Page","Invalid page");Assert.IsType<BadRequestObjectResult>(await c.Index(f.F.Org,new(),default));Assert.Single(f.F.Handler.Requests);Assert.Equal("GET",f.F.Handler.Requests[0].Method);
        await Assert.ThrowsAsync<WebApiException>(()=>f.Staff().Index(Guid.NewGuid(),new(),default));
        f.F.Handler.Role="Student";await Assert.ThrowsAsync<WebApiException>(()=>f.Api.StaffReports(f.F.Session,f.F.Org,new(),default));
        f.F.Clock.Now=f.F.Session.ExpiresAt.AddSeconds(1);Assert.Equal(401,(await Assert.ThrowsAsync<WebApiException>(()=>f.Learner().Index(f.F.Org,new(),default))).Status);
    }
    [Fact] public void APIs_are_organization_scoped_and_all_new_MVC_actions_are_GET_only()
    {
        foreach(var type in new[]{typeof(LMS.Api.Controllers.ReportsController),typeof(LMS.Api.Controllers.GradebookController)}){Assert.True(Attribute.IsDefined(type,typeof(OrganizationScopeAttribute)));Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());}
        foreach(var type in new[]{typeof(Reports),typeof(Gradebook)})foreach(var m in type.GetMethods(BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public)){Assert.NotNull(m.GetCustomAttribute<HttpGetAttribute>());Assert.Null(m.GetCustomAttribute<HttpPostAttribute>());}
    }
    [Theory][InlineData("",1,20)][InlineData("?page=2&pageSize=10&search=needle&enrollmentStatus=2",2,10)]
    public async Task Actual_API_record_query_binding_keeps_defaults_and_binds_pagination(string query,int page,int size)
    {
        using var services=LearnerWebTests.RenderServices();using var s=services.CreateScope();var h=new DefaultHttpContext{RequestServices=s.ServiceProvider};h.Request.QueryString=new QueryString(query);
        var metadata=s.ServiceProvider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(LMS.Application.Features.Reporting.ReportFilter));
        var factory=s.ServiceProvider.GetRequiredService<IModelBinderFactory>();var info=new BindingInfo{BindingSource=BindingSource.Query};var binder=factory.CreateBinder(new(){Metadata=metadata,BindingInfo=info,CacheToken=typeof(LMS.Application.Features.Reporting.ReportFilter)});
        var action=new ActionContext(h,new RouteData(),new ActionDescriptor());var provider=new QueryStringValueProvider(BindingSource.Query,h.Request.Query,System.Globalization.CultureInfo.InvariantCulture);
        var binding=DefaultModelBindingContext.CreateBindingContext(action,provider,metadata,info,"");binding.IsTopLevelObject=true;await binder.BindModelAsync(binding);
        Assert.True(binding.Result.IsModelSet);var filter=Assert.IsType<LMS.Application.Features.Reporting.ReportFilter>(binding.Result.Model);Assert.Equal(page,filter.Page);Assert.Equal(size,filter.PageSize);filter.Validate();
    }
    [Fact] public async Task Organization_middleware_replaces_forged_global_role_for_report_endpoints()
    {
        var organization=new Organization("A","Org");var user=Guid.NewGuid();var repo=new Mock<IOrganizationRepository>();repo.Setup(x=>x.GetAsync(organization.Id,default)).ReturnsAsync(organization);repo.Setup(x=>x.GetMembershipAsync(organization.Id,user,default)).ReturnsAsync(new OrganizationMembership(organization.Id,user,OrganizationRole.Student));repo.Setup(x=>x.IsSuperAdminAsync(user,default)).ReturnsAsync(false);
        var scope=new OrganizationContext();var http=new DefaultHttpContext();http.Request.Headers["X-Organization-Id"]=organization.Id.ToString();http.User=new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user.ToString()),new Claim(ClaimTypes.Role,"Admin")],"test"));http.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new OrganizationScopeAttribute()),"reports"));
        var ran=false;await new OrganizationMiddleware(h=>{ran=true;Assert.False(h.User.IsInRole("Admin"));Assert.True(h.User.IsInRole("Student"));return Task.CompletedTask;}).InvokeAsync(http,new OrganizationHandlers(repo.Object,scope),scope,repo.Object);Assert.True(ran);Assert.Equal(OrganizationRole.Student,scope.Role);
    }
    private static async Task<string> Render(string path,object model)
    {
        using var services=LearnerWebTests.RenderServices();using var s=services.CreateScope();var h=new DefaultHttpContext{RequestServices=s.ServiceProvider};h.Request.Scheme="https";var action=new ActionContext(h,new RouteData(),new ActionDescriptor());var engine=s.ServiceProvider.GetRequiredService<IRazorViewEngine>();var view=engine.GetView(null,$"/Views/{path}.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(h,s.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
