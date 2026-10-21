using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;
public sealed class BffStaffAssessmentTests
{
    private sealed class Fixture:IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api=new(){CurrentRole="Instructor"};
        public WebApplication App=null!;public InProcessServer Server=null!;public Browser Browser=null!;
        public readonly Guid Quiz=Guid.NewGuid(),Question=Guid.NewGuid(),Option=Guid.NewGuid(),Assignment=Guid.NewGuid(),Submission=Guid.NewGuid();
        public bool Owns=true,WrongParent,WrongQuiz,WrongQuestion,WrongOption,WrongAssignment,WrongSubmission,Frozen,Graded,NoQuiz,Ambiguous;
        public string? Error;public int ErrorStatus=409;
        public DateTimeOffset Due=DateTimeOffset.Parse("2026-12-01T10:20:30.1234567Z");
        public readonly List<(string Path,string Method,string Body)> Writes=[];public readonly List<string> Reads=[];
        public string Root=>$"/bff/staff/courses/{Api.Course}/sections/{Api.Section}/lessons/{Api.Lesson}";
        public string Q=>Root+"/quiz/"+Quiz;
        public string A=>Root+"/assignments/"+Assignment;
        public string Work=>A+"/submissions/"+Submission;
        public static async Task<Fixture> Create(){var f=new Fixture();f.Api.StudentResponse=f.Respond;(f.App,f.Server)=await InProcessMvcTests.Host(f.Api);f.Browser=new(f.Server);f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App,f.Api,"Student"));return f;}
        private static HttpResponseMessage Data(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data=value})};
        private AssignmentView AssignmentRow()=>new(Assignment,WrongAssignment?Guid.NewGuid():Api.Lesson,"Assignment","Exact\r\nInstructions",Due,9999999999999999.99m,false,Due,null,Frozen);
        private AssignmentSubmissionView WorkRow()=>new(Submission,WrongSubmission?Guid.NewGuid():Assignment,Guid.NewGuid(),"<script>submitted</script>",true,"private-name.pdf",Due,"Snapshot",10,Graded?new(0,"<script>feedback</script>",Api.User,Due):null);
        private HttpResponseMessage? Respond(HttpRequestMessage r)
        {
            var path=r.RequestUri!.AbsolutePath;Assert.Equal(Api.AccessToken,r.Headers.Authorization!.Parameter);Assert.Equal(Api.Organization.ToString(),Assert.Single(r.Headers.GetValues("X-Organization-Id")));
            if(r.Method!=HttpMethod.Get&&r.Method!=HttpMethod.Head)
            {
                var body=r.Content?.ReadAsStringAsync().GetAwaiter().GetResult()??"";Writes.Add((path,r.Method.Method,body));
                if(Ambiguous)throw new HttpRequestException("PRIVATE transport diagnostic");
                if(Error is not null)return new((HttpStatusCode)ErrorStatus){Content=JsonContent.Create(new {message=Error})};
                if(path.EndsWith("/grade")){if(Graded)return new(HttpStatusCode.Conflict){Content=JsonContent.Create(new {message="This submission has already been graded. The original grade is retained."})};Graded=true;}
                return Data(new {});
            }
            Reads.Add(path);
            if(path==$"/api/Courses/{Api.Course}")return Data(new CourseView(Api.Course,"Course","",false,Owns?Api.User:Guid.NewGuid()));
            if(path==$"/api/Courses/{Api.Course}/sections")return Data(new[]{new SectionView(Api.Section,WrongParent?Guid.NewGuid():Api.Course,"Section",1)});
            if(path==$"/api/Sections/{Api.Section}/lessons")return Data(new[]{new LessonView(Api.Lesson,Api.Section,"Lesson",null,false,1)});
            if(path==$"/api/Lessons/{Api.Lesson}/content-items")return Data(new ContentView([],null));
            if(path==$"/{Api.Lesson}/quiz")return NoQuiz?new(HttpStatusCode.NotFound):Data(new QuizView(Quiz,WrongQuiz?Guid.NewGuid():Api.Lesson,"Quiz",null,50,15,2,false));
            if(path==$"/api/Quizzes/{Quiz}/questions")return Data(new[]{new QuestionView(Question,WrongQuestion?Guid.NewGuid():Quiz,"Question",1,1,2)});
            if(path==$"/api/Quizzes/{Quiz}/questions/{Question}/options")return Data(new[]{new StaffOptionView(Option,WrongOption?Guid.NewGuid():Question,"Option",true,1)});
            var a=$"/api/Lessons/{Api.Lesson}/assignments";
            if(path==a+"/context")return Data(new AssignmentContextView(new(Api.Lesson,WrongParent?Guid.NewGuid():Api.Section,Api.Course,Api.Organization,Owns?Api.User:Guid.NewGuid(),false,false),null));
            if(path==a)return Data(new[]{AssignmentRow()});
            if(path==a+$"/{Assignment}")return Data(AssignmentRow());
            if(path==a+$"/{Assignment}/submissions/page")return Data(new StaffSubmissionPage(1,20,1,[WorkRow()]));
            if(path==a+$"/{Assignment}/submissions/{Submission}")return Data(WorkRow());
            if(path.EndsWith("/attachment")){var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent("%PDF-1.7 synthetic"u8.ToArray())};result.Content.Headers.ContentType=new("application/pdf");return result;}
            throw new InvalidOperationException("Unexpected assessment path "+path);
        }
        public async Task<InProcessServer.Response> Change(string path,object body,string method="POST")=>await Browser.Send(path,method,body,await Browser.Csrf());
        public async ValueTask DisposeAsync(){await App.DisposeAsync();Api.Dispose();}
    }
    [Theory][InlineData("Instructor",false)][InlineData("Admin",false)][InlineData("Student",true)]
    public async Task Reads_are_authorized_readonly_bounded_and_keep_staff_history(string role,bool platform)
    {
        await using var f=await Fixture.Create();f.Api.CurrentRole=role;f.Api.Platform=platform;f.Graded=true;f.Frozen=true;
        foreach(var path in new[]{f.Root+"/quiz",f.Root+"/assignments",f.A,f.A+"/submissions?page=1&pageSize=20",f.Work})
        {var r=await f.Browser.Send(path);Assert.Equal(200,r.Status);Assert.Contains("no-store",r.Headers.CacheControl.ToString());Assert.DoesNotContain(f.Api.AccessToken,r.Body);}
        var work=Json(await f.Browser.Send(f.Work));Assert.Equal("0",work.GetProperty("submission").GetProperty("grade").GetProperty("marks").GetString());
        Assert.Equal("9999999999999999.99",work.GetProperty("assignment").GetProperty("maximumMarks").GetString());Assert.Empty(f.Writes);
        Assert.DoesNotContain(f.Reads,x=>x.EndsWith("/submissions")||x.Contains("/Enrollments")||x.Contains("progress"));
    }
    [Theory][InlineData("student")][InlineData("owner")][InlineData("tenant")][InlineData("parent")][InlineData("quiz")][InlineData("question")][InlineData("option")][InlineData("assignment")][InlineData("submission")]
    public async Task Live_scope_and_all_stored_nested_relationships_prevent_writes(string denial)
    {
        await using var f=await Fixture.Create();switch(denial){case "student":f.Api.CurrentRole="Student";break;case "owner":f.Owns=false;break;case "tenant":f.Api.OrganizationDenied=true;break;case "parent":f.WrongParent=true;break;case "quiz":f.WrongQuiz=true;break;case "question":f.WrongQuestion=true;break;case "option":f.WrongOption=true;break;case "assignment":f.WrongAssignment=true;break;case "submission":f.WrongSubmission=true;break;}
        var assignment=denial is "assignment" or "submission";
        var r=await f.Change(assignment?f.Work+"/grade":f.Q+$"/questions/{f.Question}/options/{f.Option}/delete",assignment?new {confirmed=true,marks=0,feedback=""}:(object)new {confirmed=true});
        Assert.Contains(r.Status,new[]{403,404});Assert.Empty(f.Writes);
    }
    [Fact] public async Task Csrf_confirmations_and_nested_route_IDs_are_required()
    {
        await using var f=await Fixture.Create();
        Assert.Equal(400,(await f.Browser.Send(f.Q+"/publication","POST",new {confirmed=true,action="publish"})).Status);
        foreach(var path in new[]{f.Q+"/publication",f.Q+"/delete",f.Q+$"/questions/{f.Question}/delete",f.A+"/publication",f.Work+"/grade"})Assert.Equal(400,(await f.Change(path,new {confirmed=false,action="publish",marks=0})).Status);
        Assert.Equal(404,(await f.Change(f.Root+"/quiz/"+Guid.NewGuid()+"/publication",new {confirmed=true,action="publish"})).Status);Assert.Empty(f.Writes);
    }
    [Fact] public async Task Quiz_creation_is_explicit_and_duplicate_create_does_not_update_existing_quiz()
    {
        await using var f=await Fixture.Create();f.NoQuiz=true;var body=new {title="Create",passingPercentage=50,timeLimitInMinutes=15,maxAttempts=1};
        Assert.Equal(204,(await f.Change(f.Root+"/quiz",body)).Status);Assert.Equal("POST",Assert.Single(f.Writes).Method);
        f.NoQuiz=false;Assert.Equal(409,(await f.Change(f.Root+"/quiz",body)).Status);Assert.Single(f.Writes);
    }
    [Fact] public async Task Quiz_payloads_and_existing_methods_are_reused()
    {
        await using var f=await Fixture.Create();
        Assert.Equal(204,(await f.Change(f.Q,new {title="Edit",passingPercentage="75.12345678901234567890123456",timeLimitInMinutes=30,maxAttempts=3},"PUT")).Status);
        Assert.Equal(75.12345678901234567890123456m,JsonDocument.Parse(f.Writes.Last().Body).RootElement.GetProperty("passingPercentage").GetDecimal());
        foreach(var type in new[]{1,2,3})Assert.Equal(204,(await f.Change(f.Q+"/questions",new {questionText="Text",questionType=type,displayOrder=2,marks="0.1234567890123456789012345678"})).Status);
        Assert.Equal(0.1234567890123456789012345678m,JsonDocument.Parse(f.Writes.Last().Body).RootElement.GetProperty("marks").GetDecimal());
        Assert.Equal(204,(await f.Change(f.Q+$"/questions/{f.Question}/options/{f.Option}",new {optionText="Option",isCorrect=true,displayOrder=2},"PUT")).Status);
        Assert.Equal(204,(await f.Change(f.Q+"/publication",new {action="publish",confirmed=true})).Status);
        Assert.Equal("PATCH",f.Writes.Last().Method);Assert.Equal($"/{f.Api.Lesson}/quiz/publish",f.Writes.Last().Path);
        Assert.Equal(204,(await f.Change(f.Q+"/delete",new {confirmed=true})).Status);Assert.Equal("DELETE",f.Writes.Last().Method);
        Assert.DoesNotContain(f.Writes,x=>x.Path.Contains("attempts"));
    }
    [Fact] public async Task Title_only_edits_preserve_exact_due_ticks_newlines_and_decimal_maximum()
    {
        await using var f=await Fixture.Create();f.Frozen=true;
        var response=await f.Change(f.A,new {title="New title",instructions="Browser normalized",maximumMarks="1",dueAtUtc="2026-12-01T10:20:00Z",protectedFieldsChanged=false,dueAtChanged=false},"PUT");Assert.Equal(204,response.Status);
        var body=JsonDocument.Parse(Assert.Single(f.Writes).Body).RootElement;
        Assert.Equal(f.Due,body.GetProperty("dueAtUtc").GetDateTimeOffset());Assert.Equal("Exact\r\nInstructions",body.GetProperty("instructions").GetString());Assert.Equal(9999999999999999.99m,body.GetProperty("maximumMarks").GetDecimal());
        f.Error="Instructions, due date and maximum marks are frozen once submissions exist.";
        response=await f.Change(f.A,new {title="New",instructions="Changed",maximumMarks="10.00",protectedFieldsChanged=true},"PUT");Assert.Equal(409,response.Status);Assert.Contains("frozen once",Json(response).GetProperty("message").GetString());
    }
    [Theory][InlineData("Add at least one question before publishing or attempting the quiz.",true)][InlineData("Question 3 must have exactly one correct option.",true)][InlineData("Quiz questions, options, passing percentage, time limit and deletion are locked once an attempt exists.",true)][InlineData("PRIVATE token/path diagnostics",false)]
    public async Task Publication_and_edit_errors_use_an_allowlist(string message,bool visible)
    {
        await using var f=await Fixture.Create();f.Error=message;var r=await f.Change(f.Q+"/publication",new {action="publish",confirmed=true});Assert.Equal(409,r.Status);Assert.Equal(visible,r.Body.Contains(message));Assert.Single(f.Writes);
    }
    [Fact] public async Task Grade_zero_is_final_and_repeat_conflicts_with_original_retained()
    {
        await using var f=await Fixture.Create();Assert.Equal(204,(await f.Change(f.Work+"/grade",new {marks="0.00",feedback="Feedback",confirmed=true})).Status);
        Assert.Equal(0,JsonDocument.Parse(f.Writes[0].Body).RootElement.GetProperty("marks").GetDecimal());Assert.Equal(409,(await f.Change(f.Work+"/grade",new {marks="5.00",feedback="Override",confirmed=true})).Status);
        Assert.Equal("0",Json(await f.Browser.Send(f.Work)).GetProperty("submission").GetProperty("grade").GetProperty("marks").GetString());
    }
    [Theory][InlineData("GET")][InlineData("HEAD")]
    public async Task Downloads_are_authorized_private_PDFs_with_generated_disposition(string method)
    {
        await using var f=await Fixture.Create();var r=await f.Browser.Send(f.Work+"/attachment",method);Assert.Equal(200,r.Status);Assert.Contains("attachment",r.Headers.ContentDisposition.ToString());Assert.Contains("no-store",r.Headers.CacheControl.ToString());Assert.Equal("nosniff",r.Headers["X-Content-Type-Options"].ToString());Assert.DoesNotContain("private-name",r.Headers.ContentDisposition.ToString());
        f.Owns=false;Assert.Equal(403,(await f.Browser.Send(f.Work+"/attachment",method)).Status);
    }
    [Fact] public async Task Ambiguous_grading_is_not_replayed_and_pagination_is_bounded()
    {
        await using var f=await Fixture.Create();f.Ambiguous=true;Assert.Equal(502,(await f.Change(f.Work+"/grade",new {marks=0,feedback="",confirmed=true})).Status);await f.Browser.Send(f.Work);Assert.Single(f.Writes);
        Assert.Equal(400,(await f.Browser.Send(f.A+"/submissions?page=0&pageSize=51")).Status);
    }
}
