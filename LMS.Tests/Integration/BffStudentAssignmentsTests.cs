using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;
public sealed class BffStudentAssignmentsTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api = new(); public WebApplication App = null!; public InProcessServer Server = null!; public Browser Browser = null!;
        public readonly Guid Assignment = Guid.NewGuid(), Submission = Guid.NewGuid();
        public int State = 1, Writes, Downloads; public bool Submitted, Published = true, ForeignOwner, WrongParent, WrongAssignment, WrongSubmission, Ambiguous, BadMime, ForeignPage, ForeignGradeOwner, WrongGradeCourse;
        public int? UploadStatus; public string UploadText = ""; public bool UploadFile;
        public readonly List<string> Requests = [];
        public string Root => $"/bff/student/courses/{Api.Course}/sections/{Api.Section}/lessons/{Api.Lesson}/assignments";
        public string Detail => Root + "/" + Assignment; public string Review => Detail + "/submissions/" + Submission;
        public string Upstream => $"/api/Lessons/{Api.Lesson}/assignments";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Api.StudentResponse = f.Respond;
            (f.App, f.Server) = await InProcessMvcTests.Host(f.Api); f.Browser = new(f.Server); f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App, f.Api, "Admin")); return f;
        }
        private static HttpResponseMessage Envelope(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = value }) };
        private AssignmentView Definition() => new(Assignment, WrongAssignment ? Guid.NewGuid() : Api.Lesson, "Assignment <script>", "Instructions <img>", DateTimeOffset.UtcNow.AddDays(1), 10, Published, DateTimeOffset.UtcNow, null, Submitted);
        private AssignmentSubmissionView Work() => new(WrongSubmission ? Guid.NewGuid() : Submission, Assignment, ForeignOwner ? Guid.NewGuid() : Api.User, "Own <script> work", true, "private-name.pdf", DateTimeOffset.UtcNow, "Snapshot <img>", 10, new(0, "Feedback <script>", Guid.NewGuid(), DateTimeOffset.UtcNow));
        private GradeCourseView Course() => new(WrongGradeCourse ? Guid.NewGuid() : Api.Course, Api.Organization, ForeignGradeOwner ? Guid.NewGuid() : Api.User, "Course <img>", Published, State, new DateTime(2026,10,1,10,0,0), 100, 2, 1, 50);
        private static int Number(HttpRequestMessage request, string key, int fallback) { var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query); return values.TryGetValue(key, out var value) ? int.Parse(value.ToString()) : fallback; }
        private ReportPageView<T> Page<T>(HttpRequestMessage request, T[] rows) { var page=Number(request,"page",1); var size=Number(request,"pageSize",20); return new(ForeignPage ? Guid.NewGuid() : Api.Organization, Api.User, page,size,rows.Length,rows.Skip((page-1)*size).Take(size).ToArray()); }
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path=request.RequestUri!.AbsolutePath; Requests.Add(request.RequestUri.PathAndQuery);
            Assert.Equal(Api.Organization.ToString(),Assert.Single(request.Headers.GetValues("X-Organization-Id"))); Assert.Equal(Api.AccessToken,request.Headers.Authorization!.Parameter);
            if (request.Method==HttpMethod.Post)
            {
                Assert.Equal(Upstream+"/"+Assignment+"/submissions",path); Writes++;
                if(UploadStatus is int status)return new((HttpStatusCode)status){Content=JsonContent.Create(new{message="PRIVATE upstream diagnostic"})};
                var body=(MultipartFormDataContent)request.Content!; var fields=body.ToArray();
                UploadText=fields.Single(x=>x.Headers.ContentDisposition!.Name!.Trim('"')=="Text").ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Equal("true",fields.Single(x=>x.Headers.ContentDisposition!.Name!.Trim('"')=="Confirmed").ReadAsStringAsync().GetAwaiter().GetResult());
                UploadFile=fields.Any(x=>x.Headers.ContentDisposition!.Name!.Trim('"')=="File"); if(UploadFile)Assert.Equal("application/pdf",fields.Single(x=>x.Headers.ContentDisposition!.Name!.Trim('"')=="File").Headers.ContentType!.MediaType);
                Submitted=true;if(Ambiguous)throw new HttpRequestException("PRIVATE possible commit");return Envelope(Work());
            }
            if(path==Upstream+"/context")return Envelope(new AssignmentContextView(new(Api.Lesson,Api.Section,WrongParent?Guid.NewGuid():Api.Course,Api.Organization,Guid.NewGuid(),Published,Published),State));
            if(path==Upstream)return Envelope(new[]{Definition()});
            if(path==Upstream+"/"+Assignment)return Envelope(Definition());
            if(path==Upstream+"/"+Assignment+"/submissions/me")return Envelope(Submitted?new[]{Work()}:Array.Empty<AssignmentSubmissionView>());
            if(path==Upstream+"/"+Assignment+"/submissions/"+Submission)return Envelope(Work());
            if(path.EndsWith("/attachment"))
            {Downloads++;var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\ntest"))};response.Content.Headers.ContentType=new(BadMime?"text/html":"application/pdf");return response;}
            if(path=="/api/Assignments/submissions/me")return Envelope(Submitted&&State is 1 or 2?new[]{new AssignmentHistoryView(Api.Lesson,Api.Section,Api.Course,Assignment,Submission,"History",DateTimeOffset.UtcNow,true)}:Array.Empty<AssignmentHistoryView>());
            if(path=="/api/Gradebook/courses")return Envelope(Page(request,new[]{Course()}));
            if(path==$"/api/Gradebook/courses/{Api.Course}")return Envelope(Course());
            if(path==$"/api/Gradebook/courses/{Api.Course}/quizzes")return Envelope(Page(request,new[]{new QuizGradeView(Api.Course,Api.Section,Api.Lesson,Guid.NewGuid(),"Quiz zero",Guid.NewGuid(),1,2,DateTime.UtcNow,null,null,0,10,70,false,false,Published,true)}));
            if(path==$"/api/Gradebook/courses/{Api.Course}/assignments")return Envelope(Page(request,new[]{new AssignmentGradebookView(Api.Course,Api.Section,Api.Lesson,Assignment,"Grade zero",Submission,DateTime.UtcNow,DateTime.UtcNow,10,0,"Feedback <script>",2,Published)}));
            throw new InvalidOperationException("Unexpected request: "+path);
        }
        public async Task<InProcessServer.Response> Upload(bool csrf=true, string text="Final text", bool file=false, bool confirmed=true, string? extra=null, string filename="work.pdf", string mime="application/pdf", int files=1)
        {
            var token=csrf?await Browser.Csrf():null;using var body=new MultipartFormDataContent(); body.Add(new StringContent(text),"Text");body.Add(new StringContent(confirmed?"true":"false"),"Confirmed");if(extra is not null)body.Add(new StringContent("injected"),extra);
            if(file)for(var i=0;i<files;i++){var part=new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\ntest"));part.Headers.ContentType=new(mime);body.Add(part,"File",filename);}
            return await Server.Send(Detail+"/submit","POST",Browser.Cookie,csrf:token,rawBody:await body.ReadAsByteArrayAsync(),contentType:body.Headers.ContentType!.ToString());
        }
        public async ValueTask DisposeAsync(){await App.DisposeAsync();Api.Dispose();}
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Confirmed_multipart_text_or_PDF_uses_existing_client_and_stored_recovery(bool pdf)
    {await using var f=await Fixture.Create();Assert.Equal(204,(await f.Upload(text:pdf?"":"Final text",file:pdf)).Status);Assert.Equal(pdf,f.UploadFile);Assert.Equal(pdf?"":"Final text",f.UploadText);Assert.Equal(1,f.Writes);var read=await f.Browser.Send(f.Detail);Assert.Equal(200,read.Status);Assert.Single(Json(read).GetProperty("submissions").EnumerateArray());}
    [Fact] public async Task CSRF_confirmation_empty_work_extra_authority_multiple_and_invalid_files_are_rejected()
    {await using var f=await Fixture.Create();Assert.Equal(400,(await f.Upload(csrf:false)).Status);Assert.Equal(400,(await f.Upload(confirmed:false)).Status);Assert.Equal(400,(await f.Upload(text:"")).Status);Assert.Equal(400,(await f.Upload(extra:"organizationId")).Status);Assert.Equal(400,(await f.Upload(file:true,files:2)).Status);Assert.Equal(400,(await f.Upload(file:true,filename:"bad.exe")).Status);Assert.Equal(400,(await f.Upload(file:true,mime:"text/html")).Status);Assert.Equal(0,f.Writes);}
    [Theory] [InlineData(400)] [InlineData(409)] [InlineData(413)]
    public async Task Deadline_and_validation_conflicts_keep_safe_status_and_do_not_replay(int status)
    {await using var f=await Fixture.Create();f.UploadStatus=status;var response=await f.Upload();Assert.Equal(status,response.Status);Assert.DoesNotContain("PRIVATE",response.Body);Assert.Equal(1,f.Writes);}
    [Fact] public async Task Duplicate_final_submission_is_conflict_before_forwarding_and_ambiguous_upload_uses_read_only_recovery()
    {await using var f=await Fixture.Create();f.Ambiguous=true;var response=await f.Upload(file:true);Assert.Equal(502,response.Status);Assert.DoesNotContain("PRIVATE",response.Body);Assert.Equal(200,(await f.Browser.Send(f.Detail)).Status);Assert.Equal(409,(await f.Upload()).Status);Assert.Equal(1,f.Writes);}
    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task Own_history_and_zero_grade_survive_unpublishing_and_downloads_stay_authorized(int state)
    {await using var f=await Fixture.Create();f.State=state;f.Published=false;f.Submitted=true;
        Assert.Equal(200,(await f.Browser.Send("/bff/student/assignments/history")).Status);var read=await f.Browser.Send(f.Review);Assert.Equal(200,read.Status);Assert.Equal(0,Json(read).GetProperty("submission").GetProperty("grade").GetProperty("marks").GetInt32());
        foreach(var key in new[]{"userId","graderId","attachmentId","filename","storagePath"})Assert.DoesNotContain(key,read.Body);
        foreach(var method in new[]{"GET","HEAD"}){var download=await f.Server.Send(f.Review+"/attachment",method,f.Browser.Cookie);Assert.Equal(200,download.Status);Assert.Equal("application/pdf",download.Headers.ContentType.ToString());Assert.Contains("attachment;",download.Headers.ContentDisposition.ToString());Assert.Contains(f.Submission.ToString("N"),download.Headers.ContentDisposition.ToString());Assert.Contains("no-store",download.Headers.CacheControl.ToString());Assert.Equal("nosniff",download.Headers["X-Content-Type-Options"].ToString());if(method=="HEAD")Assert.Empty(download.Body);}
        Assert.Equal(state==2?403:409,(await f.Upload()).Status);
    }
    [Theory] [InlineData("tenant",403)] [InlineData("role",403)] [InlineData("owner",403)] [InlineData("parent",404)] [InlineData("assignment",404)] [InlineData("submission",403)] [InlineData("cancelled",403)]
    public async Task Tenant_parent_owner_and_enrollment_gates_protect_reads_uploads_and_attachments(string denial,int status)
    {await using var f=await Fixture.Create();f.Submitted=true;f.Api.ContextMismatch=denial=="tenant";if(denial=="role")f.Api.CurrentRole="Instructor";f.ForeignOwner=denial=="owner";f.WrongParent=denial=="parent";f.WrongAssignment=denial=="assignment";f.WrongSubmission=denial=="submission";if(denial=="cancelled")f.State=3;
        Assert.Equal(status,(await f.Browser.Send(f.Review)).Status);Assert.Equal(status,(await f.Browser.Send(f.Review+"/attachment")).Status);Assert.Equal(0,f.Downloads);if(denial!="submission")Assert.Equal(status,(await f.Upload()).Status);Assert.Equal(0,f.Writes);
    }
    [Fact] public async Task Anonymous_and_wrong_MIME_downloads_fail_without_leaking_tokens()
    {await using var f=await Fixture.Create();f.Submitted=true;Assert.Equal(401,(await f.Server.Send(f.Review+"/attachment")).Status);f.BadMime=true;Assert.Equal(502,(await f.Browser.Send(f.Review+"/attachment")).Status);}
    [Fact] public async Task Gradebook_uses_bounded_pages_safe_projections_filters_and_fixed_number_of_GETs()
    {await using var f=await Fixture.Create();f.State=2;f.Published=false;
        var list=await f.Browser.Send("/bff/student/grades?page=1&pageSize=1&search=Course&published=false&enrollmentStatus=2");Assert.Equal(200,list.Status);Assert.Equal(1,Json(list).GetProperty("courses").GetProperty("pageSize").GetInt32());Assert.DoesNotContain("userId",list.Body);Assert.DoesNotContain("ownerUserId",list.Body);
        var course=await f.Browser.Send($"/bff/student/grades/courses/{f.Api.Course}?quizPage=2&assignmentPage=1&pageSize=1&quizStatus=2&assignmentStatus=2");Assert.Equal(200,course.Status);var json=Json(course);Assert.Equal(2,json.GetProperty("quizzes").GetProperty("page").GetInt32());Assert.Equal(0,json.GetProperty("assignments").GetProperty("items")[0].GetProperty("marks").GetInt32());Assert.Equal(50,json.GetProperty("course").GetProperty("currentPercentage").GetInt32());Assert.EndsWith("Z",json.GetProperty("course").GetProperty("enrollmentCompletedAt").GetString());
        Assert.Contains(f.Requests,x=>x.Contains("quizzes?page=2&pageSize=1&status=2"));Assert.Contains(f.Requests,x=>x.Contains("assignments?page=1&pageSize=1&status=2"));Assert.DoesNotContain(f.Requests,x=>x.Contains("/result")||x.Contains("/progress"));Assert.Equal(0,f.Writes);
    }
    [Theory] [InlineData("page=0")] [InlineData("pageSize=51")] [InlineData("enrollmentStatus=3")]
    public async Task Gradebook_rejects_invalid_bounds_and_learner_filters(string query)
    {await using var f=await Fixture.Create();Assert.Equal(400,(await f.Browser.Send("/bff/student/grades?"+query)).Status);Assert.Equal(400,(await f.Browser.Send($"/bff/student/grades/courses/{f.Api.Course}?assignmentStatus=3")).Status);}
    [Theory] [InlineData("page")] [InlineData("owner")] [InlineData("course")]
    public async Task Gradebook_rejects_foreign_page_scope_owner_and_course(string denial)
    {await using var f=await Fixture.Create();f.ForeignPage=denial=="page";f.ForeignGradeOwner=denial=="owner";f.WrongGradeCourse=denial=="course";Assert.Equal(403,(await f.Browser.Send($"/bff/student/grades/courses/{f.Api.Course}")).Status);}
}
