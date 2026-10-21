using System.Net;
using System.Net.Http.Json;
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
using Moq;
using Xunit;
namespace LMS.Tests;

public sealed class EnrollmentProgressWebTests
{
    private sealed class Fixture:IDisposable
    {
        public LearnerWebTests.Fixture F=new();
        public Guid Enrollment=Guid.NewGuid();
        public DateTime Timestamp=new(2026,9,1,12,0,0,DateTimeKind.Utc);
        public FaultHandler Handler;
        private HttpClient client;
        public LearnerApi Api;
        public Fixture()
        {
            Handler=new(F.Handler);client=new(Handler){BaseAddress=new("https://api.example.test/")};Api=new(client);
            State(1);Progress(1,2,1);
            F.Handler.Responses[$"/api/Lessons/{F.Lesson}/progress"]=new LessonProgressView(Guid.NewGuid(),F.Session.UserId,F.Lesson,false,null,Timestamp,25,Timestamp);
        }
        public void State(int status)
        {
            var record=new LearnerEnrollment(Enrollment,F.Course,status,Timestamp,status==2?Timestamp:null,status==2?100:0,F.Session.UserId);
            F.Handler.Responses["/api/Enrollments/me"]=new[]{record};
            F.Handler.Responses[$"/api/Enrollments/{Enrollment}"]=record;
        }
        public void Progress(int status,int total,int completed)
        {
            var percentage=total==0?0:decimal.Round((decimal)completed/total*100,2);
            F.Handler.Responses[$"/api/Courses/{F.Course}/progress"]=new CurrentCourseProgress(F.Course,F.Session.UserId,total,completed,percentage,total>0&&completed==total,status,status==2?Timestamp:null);
            F.Handler.Responses["/api/Enrollments/my-courses/progress"]=new[]{new MyCourseCounts(F.Course,total,completed,percentage,total>0&&total==completed)};
        }
        public LearnerActionsController Actions(){var http=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,Mock.Of<ITempDataProvider>())};}
        public LearnController Learn(){var http=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,Mock.Of<ITempDataProvider>())};}
        public Task<CatalogPage> Catalog()=>Api.Catalog(F.Session,F.Org,default);
        public Task<MyCoursesPage> MyCourses()=>Api.MyCourses(F.Session,F.Org,default);
        public Task<LessonProgressPanel> LessonState()=>Api.LessonState(F.Session,F.Org,F.Course,F.Section,F.Lesson,default);
        public void Dispose(){client.Dispose();F.Dispose();}
    }
    private sealed class FaultHandler(HttpMessageHandler inner):HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker=new(inner,false);
        public int Mutations;public string? Fault;public int? ErrorStatus;public string Message="Student is already enrolled in this course. <script>bad</script>";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method!=HttpMethod.Get&&request.Method!=HttpMethod.Head)
            {
                Mutations++;
                if(Fault=="network")throw new HttpRequestException("PRIVATE token diagnostic");
                if(Fault=="timeout")throw new TaskCanceledException("PRIVATE token diagnostic");
                if(Fault=="gateway")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
                if(ErrorStatus.HasValue)return Task.FromResult(new HttpResponseMessage((HttpStatusCode)ErrorStatus.Value){Content=JsonContent.Create(new{success=false,message=Message,data=(object?)null,errors=(object?)null})});
            }
            return invoker.SendAsync(request,token);
        }
    }
    [Fact]
    public async Task Catalog_and_my_courses_are_separate_and_keep_all_enrollment_states()
    {
        using var t=new Fixture();var f=t.F;
        var completed=Guid.NewGuid();var cancelled=Guid.NewGuid();var available=Guid.NewGuid();var draft=Guid.NewGuid();
        f.Handler.Courses=[new(f.Course,"Active","",true),new(completed,"Completed","",true),new(cancelled,"Cancelled","",true),new(available,"Available","",true),new(draft,"Draft","",false)];
        f.Handler.Responses["/api/Enrollments/me"]=new[]{new LearnerEnrollment(t.Enrollment,f.Course,1,t.Timestamp,null,0),new LearnerEnrollment(Guid.NewGuid(),completed,2,t.Timestamp,t.Timestamp,100),new LearnerEnrollment(Guid.NewGuid(),cancelled,3,t.Timestamp,null,25),new LearnerEnrollment(Guid.NewGuid(),draft,1,t.Timestamp,null,0)};
        var catalog=await t.Catalog();Assert.Equal(4,catalog.Courses.Length);Assert.DoesNotContain(catalog.Courses,x=>x.Course.Id==draft);Assert.Null(catalog.Courses.Single(x=>x.Course.Id==available).Enrollment);
        Assert.All(f.Handler.Requests,x=>Assert.Contains(x.Path,new[]{"/api/Courses","/api/Enrollments/me",$"/api/organizations/{f.Org}/context"}));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("progress")||x.Path.Contains("sections")||x.Path.Contains("lessons"));
        var mine=await t.MyCourses();Assert.Equal(4,mine.Courses.Length);Assert.DoesNotContain(mine.Courses,x=>x.Enrollment.CourseId==available);Assert.Contains(mine.Courses,x=>x.Enrollment.Status==3);
        var html=await Render("Learn/Courses",mine);Assert.Contains("Cancelled",html);Assert.Contains("Completed",html);Assert.Contains("Active",html);
        Assert.DoesNotContain($"href=\"/learn/{f.Org}/courses/{cancelled}\"",html);Assert.DoesNotContain($"href=\"/learn/{f.Org}/courses/{draft}\"",html);
        Assert.Equal(0,t.Handler.Mutations);
    }
    [Fact]
    public async Task Enroll_success_uses_existing_route_and_redirects_without_protected_content_reads()
    {
        using var t=new Fixture();var f=t.F;f.Handler.Responses["/api/Enrollments/me"]=Array.Empty<LearnerEnrollment>();
        var result=Assert.IsType<RedirectResult>(await t.Actions().Enroll(f.Org,f.Course,false,default));
        Assert.Equal($"/learn/{f.Org}/courses/catalog",result.Url);
        var request=f.Handler.Requests.Last();Assert.Equal($"/api/Courses/{f.Course}/enroll",request.Path);Assert.Equal("POST",request.Method);
        Assert.All(f.Handler.Requests,x=>Assert.Equal(f.Org.ToString(),x.Organization));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("progress")||x.Path.Contains("sections")||x.Path.Contains("lessons"));
        Assert.Equal(1,t.Handler.Mutations);
    }
    [Theory]
    [InlineData(400)] [InlineData(409)]
    public async Task Duplicate_or_validation_error_is_encoded_and_never_retried(int status)
    {
        using var t=new Fixture();var f=t.F;t.Handler.ErrorStatus=status;var controller=t.Actions();
        Assert.Equal($"/learn/{f.Org}/courses/recovery",Assert.IsType<RedirectResult>(await controller.Enroll(f.Org,f.Course,false,default)).Url);
        var page=Assert.IsType<LearnerRecoveryPage>(Assert.IsType<ViewResult>(await controller.Recovery(f.Org,default)).Model);
        Assert.Contains("already enrolled",page.Notice);Assert.Equal(1,t.Handler.Mutations);
        var html=await Render("LearnerActions/Recovery",page);Assert.Contains("&lt;script&gt;bad&lt;/script&gt;",html);Assert.DoesNotContain("<script>bad",html);Assert.DoesNotContain("<form",html);
    }
    [Fact]
    public async Task Reenrollment_uses_only_existing_enroll_operation_and_requires_confirmation()
    {
        using var t=new Fixture();var f=t.F;t.State(3);var controller=t.Actions();
        Assert.IsType<RedirectResult>(await controller.Enroll(f.Org,f.Course,false,default));Assert.Equal(0,t.Handler.Mutations);
        await controller.Enroll(f.Org,f.Course,true,default);Assert.Equal(1,t.Handler.Mutations);
        Assert.Equal($"/api/Courses/{f.Course}/enroll",f.Handler.Requests.Last().Path);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Method=="DELETE"||x.Path.Contains("reactivate"));
    }
    [Theory]
    [InlineData("cancel")] [InlineData("complete")]
    public async Task Enrollment_mutations_require_confirmation_and_use_existing_patch(string action)
    {
        using var t=new Fixture();var f=t.F;var controller=t.Actions();
        await controller.Enrollment(f.Org,f.Course,t.Enrollment,action,false,default);Assert.Equal(0,t.Handler.Mutations);
        await controller.Enrollment(f.Org,f.Course,t.Enrollment,action,true,default);Assert.Equal(1,t.Handler.Mutations);
        Assert.Equal($"/api/Enrollments/{t.Enrollment}/{action}",f.Handler.Requests.Last().Path);Assert.Equal("PATCH",f.Handler.Requests.Last().Method);
        Assert.Contains(f.Handler.Requests,x=>x.Path==$"/api/Enrollments/{t.Enrollment}");
    }
    [Theory]
    [InlineData("enroll","network")] [InlineData("cancel","network")] [InlineData("complete","gateway")]
    [InlineData("start","timeout")] [InlineData("update","network")] [InlineData("complete-lesson","gateway")]
    public async Task Ambiguous_failure_redirects_to_readonly_recovery_without_replay(string action,string fault)
    {
        using var t=new Fixture();var f=t.F;t.Handler.Fault=fault;var controller=t.Actions();
        IActionResult result=action switch
        {
            "enroll"=>await controller.Enroll(f.Org,f.Course,false,default),
            "cancel" or "complete"=>await controller.Enrollment(f.Org,f.Course,t.Enrollment,action,true,default),
            _=>await controller.Lesson(f.Org,f.Course,f.Section,f.Lesson,action=="complete-lesson"?"complete":action,new(){ProgressPercentage=40,Confirmed=true},default)
        };
        Assert.Equal($"/learn/{f.Org}/courses/recovery",Assert.IsType<RedirectResult>(result).Url);
        var page=Assert.IsType<LearnerRecoveryPage>(Assert.IsType<ViewResult>(await controller.Recovery(f.Org,default)).Model);
        Assert.Contains("unknown",page.Notice);Assert.Equal(1,t.Handler.Mutations);
        var html=await Render("LearnerActions/Recovery",page);Assert.DoesNotContain("<form",html);Assert.DoesNotContain("PRIVATE",html);Assert.DoesNotContain(f.Session.AccessToken,html);
        await controller.Recovery(f.Org,default);Assert.Equal(1,t.Handler.Mutations);
    }
    [Theory]
    [InlineData("start","POST","start")] [InlineData("update","PATCH","progress")] [InlineData("complete","PATCH","complete")]
    public async Task Lesson_actions_send_explicit_supported_payload_and_prg(string action,string method,string suffix)
    {
        using var t=new Fixture();var f=t.F;
        var redirect=Assert.IsType<RedirectResult>(await t.Actions().Lesson(f.Org,f.Course,f.Section,f.Lesson,action,new(){ProgressPercentage=37.5m,Confirmed=true},default));
        Assert.Equal($"/learn/{f.Org}/courses/{f.Course}/sections/{f.Section}/units/{f.Lesson}",redirect.Url);
        Assert.Equal(method,f.Handler.Requests.Last().Method);Assert.Equal($"/api/Lessons/{f.Lesson}/{suffix}",f.Handler.Requests.Last().Path);
        if(action=="update")
        {
            var payload=JsonDocument.Parse(f.Handler.Bodies.Last()).RootElement;
            Assert.Equal(37.5m,payload.GetProperty("progressPercentage").GetDecimal());
            Assert.Single(payload.EnumerateObject());
        }
        else Assert.Equal("{}",f.Handler.Bodies.Last());
        Assert.Equal(1,t.Handler.Mutations);
    }
    [Theory]
    [InlineData("start",2)] [InlineData("update",2)] [InlineData("complete",2)]
    [InlineData("start",3)] [InlineData("update",3)] [InlineData("complete",3)]
    public async Task API_denial_remains_authoritative_for_nonactive_lesson_mutations(string action,int status)
    {
        using var t=new Fixture();var f=t.F;t.State(status);t.Progress(status,2,1);t.Handler.ErrorStatus=403;
        if(status==3){f.Handler.FailurePath=$"/api/Courses/{f.Course}/progress";f.Handler.Failure=403;}
        Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>t.Actions().Lesson(f.Org,f.Course,f.Section,f.Lesson,action,new(){Confirmed=true},default))).Status);
        Assert.Equal(status==2?1:0,t.Handler.Mutations);
    }
    [Theory]
    [InlineData("organization")] [InlineData("course")] [InlineData("section")] [InlineData("lesson")]
    public async Task Mismatched_route_parents_cannot_write_lesson_progress(string mismatch)
    {
        using var t=new Fixture();var f=t.F;
        await Assert.ThrowsAsync<WebApiException>(()=>t.Actions().Lesson(mismatch=="organization"?Guid.NewGuid():f.Org,mismatch=="course"?Guid.NewGuid():f.Course,mismatch=="section"?Guid.NewGuid():f.Section,mismatch=="lesson"?Guid.NewGuid():f.Lesson,"start",new(),default));
        Assert.Equal(0,t.Handler.Mutations);
    }
    [Theory]
    [InlineData("enrollment")] [InlineData("course")] [InlineData("organization")] [InlineData("owner")]
    public async Task Enrollment_parent_scope_and_authenticated_owner_are_checked(string mismatch)
    {
        using var t=new Fixture();var f=t.F;
        if(mismatch=="owner")f.Handler.Responses[$"/api/Enrollments/{t.Enrollment}"]=new LearnerEnrollment(t.Enrollment,f.Course,1,t.Timestamp,null,0,Guid.NewGuid());
        await Assert.ThrowsAsync<WebApiException>(()=>t.Actions().Enrollment(mismatch=="organization"?Guid.NewGuid():f.Org,mismatch=="course"?Guid.NewGuid():f.Course,mismatch=="enrollment"?Guid.NewGuid():t.Enrollment,"cancel",true,default));
        Assert.Equal(0,t.Handler.Mutations);
    }
    [Theory]
    [InlineData("course-progress-user")] [InlineData("course-progress-parent")] [InlineData("lesson-progress-user")] [InlineData("lesson-progress-parent")]
    public async Task Stored_progress_ids_are_validated_before_rendering_or_mutation(string mismatch)
    {
        using var t=new Fixture();var f=t.F;
        if(mismatch.StartsWith("course"))f.Handler.Responses[$"/api/Courses/{f.Course}/progress"]=new CurrentCourseProgress(mismatch.EndsWith("parent")?Guid.NewGuid():f.Course,mismatch.EndsWith("user")?Guid.NewGuid():f.Session.UserId,1,0,0,false,1,null);
        else f.Handler.Responses[$"/api/Lessons/{f.Lesson}/progress"]=new LessonProgressView(Guid.NewGuid(),mismatch.EndsWith("user")?Guid.NewGuid():f.Session.UserId,mismatch.EndsWith("parent")?Guid.NewGuid():f.Lesson,false,null,t.Timestamp,0,t.Timestamp);
        await Assert.ThrowsAsync<WebApiException>(()=>t.LessonState());Assert.Equal(0,t.Handler.Mutations);
    }
    [Theory]
    [InlineData(0,0,false)] [InlineData(2,1,false)] [InlineData(2,2,true)]
    public async Task Course_rendering_uses_published_counts_and_nonempty_completion_rule(int total,int complete,bool eligible)
    {
        using var t=new Fixture();var f=t.F;t.Progress(1,total,complete);
        var page=Assert.IsType<SectionsPage>(Assert.IsType<ViewResult>(await t.Learn().Sections(f.Org,f.Course,default)).Model);
        Assert.Equal(eligible,page.Learning!.Progress.CanComplete);var html=await Render("Learn/Sections",page);
        Assert.Contains($"{complete} / {total} lessons",html);Assert.Equal(eligible,html.Contains("Confirm completion"));
        if(total==0)Assert.Contains("at least one published lesson",html);
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Fact]
    public async Task Completed_history_is_distinct_from_current_partial_or_zero_progress()
    {
        using var t=new Fixture();var f=t.F;t.State(2);t.Progress(2,2,1);
        var mine=await t.MyCourses();var html=await Render("Learn/Courses",mine);
        Assert.Contains("Stored enrollment percentage: 100%",html);Assert.Contains("1 / 2 lessons completed (",html);Assert.Equal(50m,mine.Courses.Single().Progress!.ProgressPercentage);Assert.Contains(t.Timestamp.ToString("u"),html);
        var unit=Assert.IsType<UnitPage>(Assert.IsType<ViewResult>(await t.Learn().Unit(f.Org,f.Course,f.Section,f.Lesson,default)).Model);
        html=await Render("Learn/Unit",unit);Assert.Contains("Historical enrollment status: Completed",html);Assert.Contains("Last accessed at:",html);
        Assert.DoesNotContain("Save progress",html);Assert.DoesNotContain("Start lesson</button>",html);Assert.DoesNotContain("Mark lesson complete</button>",html);
        t.Progress(2,0,0);var course=await t.Api.CourseLearning(f.Session,f.Org,f.Course,default);Assert.False(course.Progress.CanComplete);Assert.Equal(t.Timestamp,course.Progress.EnrollmentCompletedAt);
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Fact]
    public async Task Repeated_course_completion_conflict_is_readonly_and_preserves_timestamp()
    {
        using var t=new Fixture();var f=t.F;t.State(2);t.Progress(2,2,2);t.Handler.ErrorStatus=409;t.Handler.Message="Only active enrollments can be completed.";
        var controller=t.Actions();await controller.Enrollment(f.Org,f.Course,t.Enrollment,"complete",true,default);
        var page=Assert.IsType<LearnerRecoveryPage>(Assert.IsType<ViewResult>(await controller.Recovery(f.Org,default)).Model);
        Assert.Contains("Only active",page.Notice);Assert.Equal(t.Timestamp,(await t.Api.CourseLearning(f.Session,f.Org,f.Course,default)).Enrollment.CompletedAt);Assert.Equal(1,t.Handler.Mutations);
    }
    [Fact]
    public async Task Missing_progress_remains_unstarted_and_gets_do_not_start_or_touch_access_time()
    {
        using var t=new Fixture();var f=t.F;f.Handler.Responses.Remove($"/api/Lessons/{f.Lesson}/progress");
        var first=await t.LessonState();var second=await t.LessonState();Assert.Null(first.Lesson);Assert.Null(second.Lesson);
        var unit=Assert.IsType<UnitPage>(Assert.IsType<ViewResult>(await t.Learn().Unit(f.Org,f.Course,f.Section,f.Lesson,default)).Model);
        var html=await Render("Learn/Unit",unit);Assert.Contains("Start lesson",html);Assert.Contains("do not record progress or change LastAccessedAt",html);
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));Assert.Equal(0,t.Handler.Mutations);
    }
    [Fact]
    public async Task MVC_lesson_completion_requires_confirmation_before_sending_existing_complete_operation()
    {
        using var t=new Fixture();var f=t.F;var controller=t.Actions();
        var redirect=Assert.IsType<RedirectResult>(await controller.Lesson(f.Org,f.Course,f.Section,f.Lesson,"complete",new(),default));
        Assert.Equal($"/learn/{f.Org}/courses/recovery",redirect.Url);Assert.Equal(0,t.Handler.Mutations);
        var recovery=Assert.IsType<LearnerRecoveryPage>(Assert.IsType<ViewResult>(await controller.Recovery(f.Org,default)).Model);
        Assert.Contains("Confirm that you want to mark this lesson complete",recovery.Notice);
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
        await controller.Lesson(f.Org,f.Course,f.Section,f.Lesson,"complete",new(){Confirmed=true},default);
        Assert.Equal(1,t.Handler.Mutations);Assert.Equal("PATCH",f.Handler.Requests.Last().Method);
        Assert.Equal($"/api/Lessons/{f.Lesson}/complete",f.Handler.Requests.Last().Path);Assert.Equal("{}",f.Handler.Bodies.Last());
    }
    [Theory]
    [InlineData(false,false,0,"Not started")]
    [InlineData(true,false,0,"In progress")]
    [InlineData(true,false,37.5,"In progress")]
    [InlineData(true,true,100,"Completed")]
    [InlineData(true,true,37.5,"Completed")]
    public async Task MVC_lesson_state_is_readonly_preserves_stored_percentage_and_offers_only_relevant_actions(bool started,bool completed,double percentage,string state)
    {
        using var t=new Fixture();var f=t.F;
        if(!started)f.Handler.Responses.Remove($"/api/Lessons/{f.Lesson}/progress");
        else f.Handler.Responses[$"/api/Lessons/{f.Lesson}/progress"]=new LessonProgressView(Guid.NewGuid(),f.Session.UserId,f.Lesson,completed,completed?t.Timestamp:null,t.Timestamp,(decimal)percentage,t.Timestamp);
        var unit=Assert.IsType<UnitPage>(Assert.IsType<ViewResult>(await t.Learn().Unit(f.Org,f.Course,f.Section,f.Lesson,default)).Model);
        var html=await Render("Learn/Unit",unit);
        Assert.Contains("Lesson state: "+state,html);
        Assert.DoesNotContain("Save progress",html);Assert.DoesNotContain("Form.ProgressPercentage",html);Assert.DoesNotContain("/progress/update",html);
        Assert.Equal(!started,html.Contains("Start lesson</button>"));
        Assert.Equal(started&&!completed,html.Contains("Mark lesson complete</button>"));
        if(started)
        {
            Assert.Contains($"Stored progress: {unit.Progress!.Lesson!.ProgressPercentage}%",html);
            Assert.Contains(t.Timestamp.ToString("u"),html);
            if(!completed)
            {
                Assert.Contains("name=\"Form.Confirmed\" value=\"true\" required",html);
                Assert.Contains("__RequestVerificationToken",html);
                Assert.Contains("enrollment completion remains a separate action",html);
            }
        }
        else Assert.DoesNotContain("Stored progress:",html);
        Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));Assert.Equal(0,t.Handler.Mutations);
    }
    [Fact]
    public async Task New_forms_have_csrf_safe_text_and_readonly_recovery_has_no_mutation_form()
    {
        using var t=new Fixture();var f=t.F;f.Handler.Courses=[new(f.Course,"<script>Title</script>","<img onerror=bad>",true)];
        t.State(3);var catalog=await Render("Learn/Catalog",await t.Catalog());Assert.Contains("__RequestVerificationToken",catalog);Assert.Contains("&lt;script&gt;Title",catalog);Assert.DoesNotContain("<img onerror",catalog);
        t.State(1);var mine=await Render("Learn/Courses",await t.MyCourses());Assert.Contains("__RequestVerificationToken",mine);Assert.Contains("Confirm cancellation",mine);
        var unit=Assert.IsType<UnitPage>(Assert.IsType<ViewResult>(await t.Learn().Unit(f.Org,f.Course,f.Section,f.Lesson,default)).Model);
        var html=await Render("Learn/Unit",unit);Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain(f.Session.AccessToken,html);
        Assert.True(Attribute.IsDefined(typeof(LearnerActionsController),typeof(AutoValidateAntiforgeryTokenAttribute)));
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var csrf=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var post=new DefaultHttpContext{RequestServices=scope.ServiceProvider};post.Request.Scheme="https";post.Request.Method="POST";
        await Assert.ThrowsAsync<Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException>(()=>csrf.ValidateRequestAsync(post));
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData("Student",true)]
    public async Task Revoked_or_staff_organization_context_cannot_use_learner_catalog(string role,bool platform)
    {
        using var t=new Fixture();t.F.Handler.Role=role;t.F.Handler.Platform=platform;
        await Assert.ThrowsAsync<WebApiException>(()=>t.Catalog());Assert.Equal(0,t.Handler.Mutations);
        Assert.All(t.F.Handler.Requests,x=>Assert.EndsWith("/context",x.Path));
    }
    private static async Task<string> Render<T>(string name,T model)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var http=new DefaultHttpContext{RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,$"/Views/{name}.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary<T>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
