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
using Xunit;
namespace LMS.Tests;

public sealed class QuizWebTests
{
    private sealed class QuizFixture:IDisposable
    {
        public LearnerWebTests.Fixture F=new();
        public Guid Quiz=Guid.NewGuid(),Question=Guid.NewGuid(),Option=Guid.NewGuid(),Attempt=Guid.NewGuid();
        public DateTime Deadline=DateTime.UtcNow.AddMinutes(8);
        public FaultHandler Handler;
        public HttpClient Client;
        public LearnerApi Api;
        public QuizFixture()
        {
            Handler=new(F.Handler);Client=new(Handler){BaseAddress=new("https://api.example.test/")};Api=new(Client);
            F.Handler.Responses[$"/{F.Lesson}/quiz"]=new QuizView(Quiz,F.Lesson,"<script>Quiz</script>","Description",70,10,2,false);
            F.Handler.Responses[$"/api/Quizzes/{Quiz}/questions"]=new[]{new QuestionView(Question,Quiz,"<script>Question</script>",1,1,2)};
            F.Handler.Responses[$"/api/Quizzes/{Quiz}/questions/{Question}/options"]=new[]{new StaffOptionView(Option,Question,"<script>Option</script>",true,1)};
            F.Handler.Responses[$"/api/Quizzes/{Quiz}/questions/{Question}/options/student"]=new[]{new LearnerOptionView(Option,"<script>Option</script>",1)};
            History(1);
        }
        public void History(int status)=>F.Handler.Responses[$"/api/Quizzes/{Quiz}/attempts"]=new[]{new QuizAttemptView(Attempt,1,status,Deadline,status==2?2:null,2,status==2?true:null)};
        public Task<QuizEditorPage> Editor()=>Api.QuizEditor(F.Session,F.Org,F.Course,F.Section,F.Lesson,default);
        public Task<LearnerQuizPage> Learner(Guid? attempt=null)=>Api.LearnerQuiz(F.Session,F.Org,F.Course,F.Section,F.Lesson,attempt,default);
        public QuizAuthoringController Staff(){F.Handler.Role="Instructor";var http=F.Http();http.Request.Path=$"/staff/{F.Org}/courses/{F.Course}/sections/{F.Section}/lessons/{F.Lesson}/quiz/change";return new(Api,F.Sessions){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,Moq.Mock.Of<ITempDataProvider>())};}
        public QuizLearningController Student(string suffix){var http=F.Http();http.Request.Path=$"/learn/{F.Org}/courses/{F.Course}/sections/{F.Section}/units/{F.Lesson}/quiz/{suffix}";return new(Api,F.Sessions){ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,Moq.Mock.Of<ITempDataProvider>())};}
        public void Dispose(){Client.Dispose();F.Dispose();}
    }
    private sealed class FaultHandler(HttpMessageHandler inner):HttpMessageHandler
    {
        private readonly HttpMessageInvoker invoker=new(inner,false);
        public string? Fault;public string? ResponsePath;public object? ResponseData;public int Mutations;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method!=HttpMethod.Get)
            {
                Mutations++;
                if(Fault=="network")throw new HttpRequestException("PRIVATE token diagnostics");
                if(Fault=="gateway")return new(HttpStatusCode.BadGateway);
                if(request.RequestUri!.AbsolutePath==ResponsePath)return new(HttpStatusCode.Created){Content=JsonContent.Create(new{success=true,data=ResponseData})};
            }
            return await invoker.SendAsync(request,token);
        }
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Authoring_reuses_exact_routes_and_selected_scope(string? role,bool platform)
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Role=role;f.Handler.Platform=platform;
        var page=await q.Editor();await q.Api.SaveQuiz(f.Session,page,page.Form,default);
        Assert.Equal($"/{f.Lesson}/quiz",f.Handler.Requests.Last().Path);Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        await q.Api.QuizChange(f.Session,page,"question",null,null,new QuestionForm{QuestionText="Q",QuestionType=2,DisplayOrder=2,Marks=3},default);
        Assert.Equal($"/api/Quizzes/{q.Quiz}/questions",f.Handler.Requests.Last().Path);
        await q.Api.QuizChange(f.Session,page,"question",q.Question,null,new QuestionForm{QuestionText="Edited"},default);
        Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        await q.Api.QuizChange(f.Session,page,"option",q.Question,null,new OptionForm{OptionText="New"},default);
        Assert.Equal($"/api/Quizzes/{q.Quiz}/questions/{q.Question}/options",f.Handler.Requests.Last().Path);
        await q.Api.QuizChange(f.Session,page,"option",q.Question,q.Option,new OptionForm{OptionText="Edited",IsCorrect=true},default);
        Assert.Equal("PUT",f.Handler.Requests.Last().Method);
        await q.Api.QuizChange(f.Session,page,"delete",q.Question,q.Option,null,default);
        Assert.Equal("DELETE",f.Handler.Requests.Last().Method);
        await q.Api.QuizChange(f.Session,page,"delete",q.Question,null,null,default);
        Assert.Equal($"/api/Quizzes/{q.Quiz}/questions/{q.Question}",f.Handler.Requests.Last().Path);
        f.Handler.Responses.Remove($"/{f.Lesson}/quiz");page=await q.Editor();await q.Api.SaveQuiz(f.Session,page,page.Form,default);
        Assert.Equal("POST",f.Handler.Requests.Last().Method);
        Assert.All(f.Handler.Requests,x=>Assert.Equal(f.Org.ToString(),x.Organization));
    }
    [Theory]
    [InlineData("publish")] [InlineData("unpublish")] [InlineData("delete")]
    public async Task Confirmation_is_required_on_server_and_validation_is_visible(string action)
    {
        using var q=new QuizFixture();var f=q.F;var controller=q.Staff();
        var result=Assert.IsType<ViewResult>(await controller.Change(f.Org,f.Course,f.Section,f.Lesson,action,q.Question,null,new(),new(),false,default));
        Assert.Equal(0,q.Handler.Mutations);Assert.Contains(controller.ModelState.Values.SelectMany(x=>x.Errors),x=>x.ErrorMessage.Contains("confirmation"));
        controller=q.Staff();f.Handler.ValidationPath=action=="delete"?$"/api/Quizzes/{q.Quiz}/questions/{q.Question}":$"/{f.Lesson}/quiz/{action}";
        Assert.IsType<ViewResult>(await controller.Change(f.Org,f.Course,f.Section,f.Lesson,action,q.Question,null,new(),new(),true,default));
        Assert.Contains(controller.ModelState.Values.SelectMany(x=>x.Errors),x=>x.ErrorMessage.Contains("Title is invalid"));
    }
    [Fact]
    public async Task Publication_ignores_unsubmitted_dto_validation_but_keeps_confirmation_validation()
    {
        using var q=new QuizFixture();var f=q.F;var controller=q.Staff();
        controller.ModelState.AddModelError("question.QuestionText","Required");controller.ModelState.AddModelError("option.OptionText","Required");
        Assert.IsType<RedirectResult>(await controller.Change(f.Org,f.Course,f.Section,f.Lesson,"publish",null,null,new(),new(),true,default));
        Assert.Equal(1,q.Handler.Mutations);Assert.Equal("PATCH",f.Handler.Requests.Last().Method);
        Assert.Equal($"/{f.Lesson}/quiz/publish",f.Handler.Requests.Last().Path);
    }
    [Fact]
    public async Task Safe_quiz_and_nested_form_values_survive_validation()
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Role="Instructor";f.Handler.ValidationPath=$"/{f.Lesson}/quiz";
        var controller=q.Staff();var form=new QuizForm{Title="<script>Entered</script>",Description="Body",MaxAttempts=3};
        var result=Assert.IsType<ViewResult>(await controller.Save(f.Org,f.Course,f.Section,f.Lesson,form,default));
        Assert.Same(form,Assert.IsType<QuizEditorPage>(result.Model).Form);
        controller=q.Staff();f.Handler.ValidationPath=$"/api/Quizzes/{q.Quiz}/questions/{q.Question}";
        var question=new QuestionForm{QuestionText="<script>Entered</script>",QuestionType=3,Marks=4};
        result=Assert.IsType<ViewResult>(await controller.Change(f.Org,f.Course,f.Section,f.Lesson,"question",q.Question,null,question,new(),false,default));
        Assert.Same(question,controller.ViewData["PendingQuestion"]);
        var html=await Render("QuizAuthoring/Index",(QuizEditorPage)result.Model!,controller.ViewData);
        Assert.Contains("&lt;script&gt;Entered&lt;/script&gt;",html);Assert.DoesNotContain("<script>Entered",html);
    }
    [Theory]
    [InlineData("organization")] [InlineData("course")] [InlineData("section")] [InlineData("lesson")] [InlineData("quiz")] [InlineData("question")] [InlineData("option")]
    public async Task Mismatched_parents_prevent_any_authoring_write(string mismatch)
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Role="Instructor";
        if(mismatch=="quiz")f.Handler.Responses[$"/{f.Lesson}/quiz"]=new QuizView(q.Quiz,Guid.NewGuid(),"Bad",null,70,10,2,false);
        if(mismatch=="question")f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/questions"]=new[]{new QuestionView(q.Question,Guid.NewGuid(),"Bad",1,1,1)};
        if(mismatch=="option")f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/questions/{q.Question}/options"]=new[]{new StaffOptionView(q.Option,Guid.NewGuid(),"Bad",true,1)};
        await Assert.ThrowsAsync<WebApiException>(()=>q.Api.QuizEditor(f.Session,mismatch=="organization"?Guid.NewGuid():f.Org,mismatch=="course"?Guid.NewGuid():f.Course,mismatch=="section"?Guid.NewGuid():f.Section,mismatch=="lesson"?Guid.NewGuid():f.Lesson,default));
        Assert.Equal(0,q.Handler.Mutations);
    }
    [Theory]
    [InlineData("Instructor",false)] [InlineData("Admin",false)] [InlineData(null,true)]
    public async Task Foreign_scope_and_unselected_superadmin_are_rejected(string? role,bool platform)
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Role=role;f.Handler.Platform=platform;
        await Assert.ThrowsAsync<WebApiException>(()=>q.Api.QuizEditor(f.Session,Guid.NewGuid(),f.Course,f.Section,f.Lesson,default));
        var unselected=new LearnerSession{AccessToken=f.Session.AccessToken,UserId=f.Session.UserId,IsSuperAdmin=true};
        await Assert.ThrowsAsync<WebApiException>(()=>q.Api.QuizEditor(unselected,f.Org,f.Course,f.Section,f.Lesson,default));
        Assert.Empty(f.Handler.Requests);
        if(role=="Instructor"){f.Handler.OwnsCourse=false;await Assert.ThrowsAsync<WebApiException>(()=>q.Editor());}
        else {f.Handler.FailurePath=$"/api/organizations/{f.Org}/context";await Assert.ThrowsAsync<WebApiException>(()=>q.Editor());}
        Assert.Equal(0,q.Handler.Mutations);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Learner_uses_safe_options_and_exact_answer_payload(int type)
    {
        using var q=new QuizFixture();var f=q.F;
        f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/questions"]=new[]{new QuestionView(q.Question,q.Quiz,"Q",type,1,2)};
        var page=await q.Learner(q.Attempt);Assert.Equal(q.Deadline,page.Attempt!.Deadline);
        Assert.Contains(type==2?"type=\"checkbox\"":"type=\"radio\"",await Render("QuizLearning/Index",page));
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.EndsWith("/options"));
        var form=new QuizSubmission{Answers=[new(){QuestionId=q.Question,SelectedOptionIds=[q.Option]}]};
        await q.Api.SubmitQuiz(f.Session,page,form,default);
        var body=JsonDocument.Parse(f.Handler.Bodies.Last()).RootElement;
        Assert.Equal(q.Question,body.GetProperty("answers")[0].GetProperty("questionId").GetGuid());
        Assert.Equal(q.Option,body.GetProperty("answers")[0].GetProperty("selectedOptionIds")[0].GetGuid());
        Assert.DoesNotContain("isCorrect",f.Handler.Bodies.Last(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"/api/Quizzes/{q.Quiz}/attempts/{q.Attempt}/submit",f.Handler.Requests.Last().Path);
        Assert.DoesNotContain(typeof(LearnerOptionView).GetProperties(),x=>x.Name.Contains("Correct"));
    }
    [Fact]
    public async Task Start_resumes_history_without_another_start_and_new_start_uses_api()
    {
        using var q=new QuizFixture();var f=q.F;var page=await q.Learner();
        Assert.Equal(q.Attempt,await q.Api.StartQuiz(f.Session,page,default));Assert.Equal(0,q.Handler.Mutations);
        f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/attempts"]=Array.Empty<QuizAttemptView>();
        q.Handler.ResponsePath=$"/api/Quizzes/{q.Quiz}/attempts/start";q.Handler.ResponseData=new QuizStartView(q.Attempt,q.Deadline,1);
        Assert.Equal(q.Attempt,await q.Api.StartQuiz(f.Session,await q.Learner(),default));Assert.Equal(1,q.Handler.Mutations);
    }
    [Theory]
    [InlineData("network")] [InlineData("gateway")]
    public async Task Ambiguous_submit_does_not_retry_or_redisplay_a_submit_form(string fault)
    {
        using var q=new QuizFixture();var f=q.F;q.Handler.Fault=fault;var controller=q.Student($"attempts/{q.Attempt}/submit");
        var redirect=Assert.IsType<RedirectResult>(await controller.Submit(f.Org,f.Course,f.Section,f.Lesson,q.Attempt,new(){Answers=[new(){QuestionId=q.Question,SelectedOptionIds=[q.Option]}]},default));
        Assert.EndsWith($"/attempts/{q.Attempt}",redirect.Url);
        var result=Assert.IsType<ViewResult>(await controller.Index(f.Org,f.Course,f.Section,f.Lesson,q.Attempt,default));
        var page=Assert.IsType<LearnerQuizPage>(result.Model);Assert.Empty(page.Questions);Assert.Contains("unknown",page.Notice);
        Assert.Equal(1,q.Handler.Mutations);var html=await Render("QuizLearning/Index",page);
        Assert.DoesNotContain("Submit answers",html);Assert.DoesNotContain("PRIVATE",html);Assert.DoesNotContain(f.Session.AccessToken,html);
    }
    [Fact]
    public async Task Ambiguous_start_has_no_automatic_retry()
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/attempts"]=Array.Empty<QuizAttemptView>();q.Handler.Fault="network";
        var controller=q.Student("start");var redirect=Assert.IsType<RedirectResult>(await controller.Start(f.Org,f.Course,f.Section,f.Lesson,default));Assert.EndsWith("/quiz",redirect.Url);
        var result=Assert.IsType<ViewResult>(await controller.Index(f.Org,f.Course,f.Section,f.Lesson,null,default));
        var page=Assert.IsType<LearnerQuizPage>(result.Model);Assert.Contains("unknown",page.Notice);Assert.Equal(1,q.Handler.Mutations);
        Assert.DoesNotContain("Start attempt",await Render("QuizLearning/Index",page));
    }
    [Fact]
    public async Task Completed_enrollment_keeps_history_and_results_and_expired_attempts_have_no_result()
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.Responses["/api/Enrollments/me"]=new[]{new EnrollmentView(f.Course,2)};q.History(2);
        f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/attempts/{q.Attempt}/result"]=new QuizResultView(q.Attempt,2,2,70,true);
        f.Handler.Responses[$"/api/Quizzes/{q.Quiz}/attempts/{q.Attempt}/answers"]=new QuizReviewView(q.Attempt,[new(q.Question,"<script>Review</script>",[new(q.Option,"<script>Selected</script>")])]);
        var page=await q.Learner(q.Attempt);Assert.False(page.Active);Assert.True(page.Result!.IsPassed);
        var html=await Render("QuizLearning/Index",page);Assert.DoesNotContain("Start attempt",html);Assert.Contains("&lt;script&gt;Selected",html);
        await Assert.ThrowsAsync<WebApiException>(()=>q.Learner(Guid.NewGuid()));
        q.History(3);f.Handler.Requests.Clear();page=await q.Learner(q.Attempt);Assert.Null(page.Result);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.EndsWith("/result")||x.Path.EndsWith("/answers"));
    }
    [Fact]
    public async Task Repeated_submission_and_deadline_rejection_remain_api_decisions()
    {
        using var q=new QuizFixture();var f=q.F;f.Handler.ValidationPath=$"/api/Quizzes/{q.Quiz}/attempts/{q.Attempt}/submit";
        q.Deadline=DateTime.UtcNow.AddMinutes(-1);q.History(1);
        var controller=q.Student($"attempts/{q.Attempt}/submit");
        var result=Assert.IsType<ViewResult>(await controller.Submit(f.Org,f.Course,f.Section,f.Lesson,q.Attempt,new(),default));
        Assert.False(controller.ModelState.IsValid);Assert.Equal(1,q.Handler.Mutations);
        Assert.Equal(q.Deadline,Assert.IsType<LearnerQuizPage>(result.Model).Attempt!.Deadline);
    }
    [Fact]
    public async Task Rendering_encodes_all_author_text_and_protects_modifying_forms()
    {
        using var q=new QuizFixture();q.F.Handler.Role="Instructor";var staff=await Render("QuizAuthoring/Index",await q.Editor());
        var student=await Render("QuizLearning/Index",await q.Learner(q.Attempt));
        foreach(var html in new[]{staff,student}){Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain("<script>Quiz",html);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain(q.F.Session.AccessToken,html);}
        Assert.DoesNotContain("Correct",student);Assert.Contains("data-quiz-deadline",student);
        Assert.True(Attribute.IsDefined(typeof(QuizAuthoringController),typeof(AutoValidateAntiforgeryTokenAttribute)));
        Assert.True(Attribute.IsDefined(typeof(QuizLearningController),typeof(AutoValidateAntiforgeryTokenAttribute)));
    }
    [Fact]
    public async Task Lesson_embeds_instructions_history_and_result_summary_without_changing_content()
    {
        using var q=new QuizFixture();var f=q.F;q.History(2);
        var controller=new LearnController(q.Api,f.Sessions){ControllerContext=new(){HttpContext=f.Http()},TempData=new TempDataDictionary(f.Http(),Moq.Mock.Of<ITempDataProvider>())};
        var result=Assert.IsType<ViewResult>(await controller.Unit(f.Org,f.Course,f.Section,f.Lesson,default));
        var unit=Assert.IsType<UnitPage>(result.Model);Assert.NotNull(unit.Quiz);Assert.Single(unit.Content.Items);
        var html=await Render("Learn/Unit",unit);Assert.Contains("Maximum attempts",html);Assert.Contains("Attempt history",html);Assert.Contains("Marks: 2 / 2",html);Assert.Contains("/items/",html);
    }
    private static async Task<string> Render<T>(string name,T model,ViewDataDictionary? source=null)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var http=new DefaultHttpContext{RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var view=engine.GetView(null,$"/Views/{name}.cshtml",false);Assert.True(view.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary<T>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        if(source is not null)foreach(var pair in source)data[pair.Key]=pair.Value;
        await view.View.RenderAsync(new ViewContext(action,view.View,data,new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
