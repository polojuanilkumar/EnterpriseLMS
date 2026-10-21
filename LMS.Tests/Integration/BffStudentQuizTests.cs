using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;

public sealed class BffStudentQuizTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api = new();
        public WebApplication App = null!; public InProcessServer Server = null!; public Browser Browser = null!;
        public readonly Guid Quiz = Guid.NewGuid(), Attempt = Guid.NewGuid(), Enrollment = Guid.NewGuid(), Question = Guid.NewGuid(), Option = Guid.NewGuid();
        public int EnrollmentState = 1, Status = 1, Mutations, ResultReads, ReviewReads, OptionReads;
        public bool Started, Ambiguous, WrongQuiz, WrongSection, WrongLesson, ForeignOwner, WrongQuestion, WrongResult, Draft;
        public DateTime Deadline = DateTime.UtcNow.AddMinutes(10);
        public bool NoQuiz;
        public int? MutationStatus;
        public string? SubmittedBody;
        public readonly List<string> Reads = [];
        public string Root => $"/bff/student/courses/{Api.Course}/sections/{Api.Section}/lessons/{Api.Lesson}/quiz";
        public string AttemptPath => Root + $"/attempts/{Attempt}";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Api.StudentResponse = f.Respond;
            (f.App, f.Server) = await InProcessMvcTests.Host(f.Api); f.Browser = new(f.Server);
            f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App, f.Api, "Admin")); return f;
        }
        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal(Api.Organization.ToString(), Assert.Single(request.Headers.GetValues("X-Organization-Id")));
            Assert.Equal(Api.AccessToken, request.Headers.Authorization!.Parameter);
            if (request.Method != HttpMethod.Get)
            {
                Assert.Equal(HttpMethod.Post, request.Method); Mutations++;
                if (MutationStatus is int status) return new((HttpStatusCode)status) { Content = JsonContent.Create(new { message = "PRIVATE diagnostic" }) };
                if (path.EndsWith("/start")) { Started = true; }
                else if (path.EndsWith("/submit"))
                {
                    SubmittedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (Status != 1 || Deadline < DateTime.UtcNow) { Status = Status == 1 ? 3 : Status; return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "PRIVATE repeated/expired" }) }; }
                    Status = 2;
                }
                else throw new InvalidOperationException("Unexpected mutation");
                if (Ambiguous) throw new HttpRequestException("PRIVATE committed transport failure");
                return path.EndsWith("/start") ? Envelope(new QuizStartView(Attempt, Deadline, 1)) : Envelope(new { });
            }
            Reads.Add(path);
            if (path == $"/api/Courses/{Api.Course}") return Envelope(new CourseView(Api.Course, "Course", "", !Draft));
            if (path == $"/api/Courses/{Api.Course}/progress") return Envelope(new { });
            if (path == "/api/Enrollments/me" || path == $"/api/Enrollments/{Enrollment}")
            {
                var record = new LearnerEnrollment(Enrollment, Api.Course, EnrollmentState, DateTime.UtcNow, null, 0, ForeignOwner ? Guid.NewGuid() : Api.User);
                return Envelope(path.EndsWith("/me") ? (object)new[] { record } : record);
            }
            if (path == $"/api/Courses/{Api.Course}/sections") return Envelope(new[] { new SectionView(Api.Section, WrongSection ? Guid.NewGuid() : Api.Course, "Section", 1) });
            if (path == $"/api/Sections/{Api.Section}/lessons") return Envelope(new[] { new LessonView(Api.Lesson, WrongLesson ? Guid.NewGuid() : Api.Section, "Lesson", null, true, 1) });
            if (path == $"/api/Lessons/{Api.Lesson}") return Envelope(new LessonView(Api.Lesson, Api.Section, "Lesson", null, true, 1));
            if (path == $"/api/Lessons/{Api.Lesson}/content-items") return Envelope(new ContentView([], null));
            if (path == $"/{Api.Lesson}/quiz" && NoQuiz) return new(HttpStatusCode.NotFound);
            if (path == $"/{Api.Lesson}/quiz") return Envelope(new QuizView(Quiz, WrongQuiz ? Guid.NewGuid() : Api.Lesson, "Quiz <script>", "Instructions", 70, 10, 2, true));
            if (path == $"/api/Quizzes/{Quiz}/attempts") return Envelope(Started ? new[] { new QuizAttemptView(Attempt, 1, Status, Deadline, Status == 2 ? 2 : 999, 2, Status == 2 ? true : false) } : Array.Empty<QuizAttemptView>());
            if (path == $"/api/Quizzes/{Quiz}/questions") return Envelope(new[] { new QuestionView(Question, WrongQuestion ? Guid.NewGuid() : Quiz, "Question <img>", 2, 1, 2) });
            if (path == $"/api/Quizzes/{Quiz}/questions/{Question}/options/student") { OptionReads++; return Envelope(new[] { new { id = Option, optionText = "Option <script>", displayOrder = 1, isCorrect = true, answerKey = "PRIVATE" } }); }
            if (path == $"/api/Quizzes/{Quiz}/attempts/{Attempt}/result") { ResultReads++; return Envelope(new QuizResultView(WrongResult ? Guid.NewGuid() : Attempt, 2, 2, 70, true)); }
            if (path == $"/api/Quizzes/{Quiz}/attempts/{Attempt}/answers") { ReviewReads++; return Envelope(new { attemptId = Attempt, answers = new[] { new { questionId = Question, questionText = "Snapshot <script>", selectedOptions = new[] { new { optionId = Option, optionText = "Selected <img>", isCorrect = true } }, isCorrect = true } } }); }
            throw new InvalidOperationException("Unexpected quiz request: " + path);
        }
        public Task<InProcessServer.Response> Start() => Change(Root + "/start", new { });
        public object Payload(bool confirmed = true, Guid? question = null, Guid? option = null) => new { confirmed, answers = new[] { new { questionId = question ?? Question, selectedOptionIds = new[] { option ?? Option } } } };
        public async Task<InProcessServer.Response> Change(string path, object body) => await Browser.Send(path, "POST", body, await Browser.Csrf());
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Api.Dispose(); }
    }
    [Fact]
    public async Task Instructions_history_and_safe_options_never_disclose_keys_or_write_progress()
    {
        await using var f = await Fixture.Create();
        foreach (var path in new[] { f.Root, f.Root + "/attempts" }) { var response = await f.Browser.Send(path); Assert.Equal(200, response.Status); Assert.Empty(Json(response).GetProperty("questions").EnumerateArray()); }
        f.Started = true;
        foreach (var path in new[] { f.AttemptPath, f.AttemptPath + "/questions" })
        {
            var response = await f.Browser.Send(path); Assert.Equal(200, response.Status);
            Assert.DoesNotContain("isCorrect", response.Body); Assert.DoesNotContain("answerKey", response.Body); Assert.DoesNotContain("PRIVATE", response.Body); Assert.DoesNotContain(f.Api.AccessToken, response.Body);
            var row = Json(response).GetProperty("questions")[0]; Assert.Equal(2, row.GetProperty("questionType").GetInt32()); Assert.Equal(f.Option, row.GetProperty("options")[0].GetProperty("id").GetGuid());
            Assert.Contains("no-store", response.Headers.CacheControl.ToString());
        }
        Assert.Equal(0, f.Mutations); Assert.Equal(0, f.ResultReads); Assert.Equal(0, f.ReviewReads);
        var history = await f.Browser.Send(f.Root); Assert.Equal(JsonValueKind.Null, Json(history).GetProperty("attempts")[0].GetProperty("earnedMarks").ValueKind);
    }
    [Fact]
    public async Task Explicit_start_then_resume_uses_one_mutation_and_the_original_server_deadline()
    {
        await using var f = await Fixture.Create(); var deadline = f.Deadline;
        Assert.Equal(200, (await f.Start()).Status); Assert.Equal(1, f.Mutations);
        Assert.Equal(200, (await f.Start()).Status); Assert.Equal(1, f.Mutations);
        var read = await f.Browser.Send(f.AttemptPath); Assert.Equal(deadline, Json(read).GetProperty("attempt").GetProperty("deadline").GetDateTime()); Assert.Equal(deadline, f.Deadline);
    }
    [Fact]
    public async Task Submission_requires_confirmation_and_safe_question_option_IDs_then_exposes_only_stored_owner_review()
    {
        await using var f = await Fixture.Create(); f.Started = true;
        Assert.Equal(400, (await f.Change(f.AttemptPath + "/submit", f.Payload(false))).Status);
        Assert.Equal(400, (await f.Change(f.AttemptPath + "/submit", f.Payload(question: Guid.NewGuid()))).Status);
        Assert.Equal(400, (await f.Change(f.AttemptPath + "/submit", f.Payload(option: Guid.NewGuid()))).Status); Assert.Equal(0, f.Mutations);
        Assert.Equal(204, (await f.Change(f.AttemptPath + "/submit", f.Payload())).Status); Assert.Equal(1, f.Mutations);
        var body = JsonDocument.Parse(f.SubmittedBody!).RootElement; Assert.False(body.TryGetProperty("confirmed", out _)); Assert.Equal(f.Question, body.GetProperty("answers")[0].GetProperty("questionId").GetGuid());
        foreach (var path in new[] { f.AttemptPath, f.AttemptPath + "/result", f.AttemptPath + "/review" })
        {
            var response = await f.Browser.Send(path); Assert.Equal(200, response.Status); Assert.DoesNotContain("isCorrect", response.Body); Assert.DoesNotContain("answerKey", response.Body);
        }
        Assert.Equal(409, (await f.Change(f.AttemptPath + "/submit", f.Payload())).Status); Assert.Equal(2, f.Mutations);
    }
    [Theory]
    [InlineData(1)] [InlineData(3)]
    public async Task Unsubmitted_or_expired_attempts_never_load_submitted_only_results_or_review(int status)
    {
        await using var f = await Fixture.Create(); f.Started = true; f.Status = status;
        Assert.Equal(409, (await f.Browser.Send(f.AttemptPath + "/result")).Status); Assert.Equal(409, (await f.Browser.Send(f.AttemptPath + "/review")).Status);
        Assert.Equal(0, f.ResultReads); Assert.Equal(0, f.ReviewReads);
    }
    [Fact]
    public async Task Deadline_expiration_is_decided_by_submit_API_and_not_by_a_GET()
    {
        await using var f = await Fixture.Create(); f.Started = true; f.Deadline = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(200, (await f.Browser.Send(f.AttemptPath)).Status); Assert.Equal(1, f.Status); Assert.Equal(0, f.Mutations);
        Assert.Equal(409, (await f.Change(f.AttemptPath + "/submit", f.Payload())).Status); Assert.Equal(3, f.Status);
        var read = await f.Browser.Send(f.AttemptPath); Assert.Equal(3, Json(read).GetProperty("attempt").GetProperty("status").GetInt32()); Assert.Equal(0, f.ResultReads);
    }
    [Fact]
    public async Task Completed_enrollment_retains_history_results_but_blocks_start_and_submit()
    {
        await using var f = await Fixture.Create(); f.EnrollmentState = 2; f.Started = true; f.Status = 2;
        Assert.Equal(200, (await f.Browser.Send(f.AttemptPath)).Status); Assert.Equal(403, (await f.Start()).Status);
        Assert.Equal(403, (await f.Change(f.AttemptPath + "/submit", f.Payload())).Status); Assert.Equal(0, f.Mutations);
    }
    [Theory]
    [InlineData("section", 404)] [InlineData("lesson", 404)] [InlineData("quiz", 404)] [InlineData("question", 404)]
    [InlineData("result", 404)] [InlineData("owner", 403)] [InlineData("tenant", 403)] [InlineData("role", 403)] [InlineData("draft", 404)]
    public async Task Stored_parent_tenant_and_owner_checks_gate_every_read_and_mutation(string denial, int status)
    {
        await using var f = await Fixture.Create(); f.Started = true;
        f.WrongSection = denial == "section"; f.WrongLesson = denial == "lesson"; f.WrongQuiz = denial == "quiz"; f.WrongQuestion = denial == "question";
        f.WrongResult = denial == "result"; if (f.WrongResult) f.Status = 2; f.ForeignOwner = denial == "owner"; f.Api.ContextMismatch = denial == "tenant"; if (denial == "role") f.Api.CurrentRole = "Instructor"; f.Draft = denial == "draft";
        Assert.Equal(status, (await f.Browser.Send(f.AttemptPath)).Status); Assert.Equal(status, (await f.Change(f.AttemptPath + "/submit", f.Payload())).Status); Assert.Equal(0, f.Mutations);
    }
    [Fact]
    public async Task Anonymous_CSRF_injected_authority_and_foreign_attempts_are_rejected()
    {
        await using var f = await Fixture.Create(); f.Started = true;
        Assert.Equal(401, (await f.Server.Send(f.Root)).Status); Assert.Equal(400, (await f.Browser.Send(f.Root + "/start", "POST", new { })).Status);
        Assert.Equal(400, (await f.Change(f.Root + "/start", new { organizationId = Guid.NewGuid() })).Status);
        Assert.Equal(400, (await f.Change(f.AttemptPath + "/submit", new { confirmed = true, answers = new[] { new { questionId = f.Question, selectedOptionIds = new[] { f.Option }, isCorrect = true } } })).Status);
        Assert.Equal(404, (await f.Browser.Send(f.Root + "/attempts/" + Guid.NewGuid())).Status);
        Assert.Equal(200, (await f.Server.Send(f.Root, cookie: f.Browser.Cookie, organizationHeader: Guid.NewGuid().ToString())).Status); Assert.Equal(0, f.Mutations);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ambiguous_commits_recover_through_owner_history_or_results_without_replay(bool submit)
    {
        await using var f = await Fixture.Create(); f.Ambiguous = true; f.Started = submit;
        var response = submit ? await f.Change(f.AttemptPath + "/submit", f.Payload()) : await f.Start(); Assert.Equal(502, response.Status); Assert.DoesNotContain("PRIVATE", response.Body);
        Assert.Equal(200, (await f.Browser.Send(f.AttemptPath)).Status); Assert.Equal(submit ? 2 : 1, f.Status); Assert.Equal(1, f.Mutations);
    }
    [Fact]
    public async Task Missing_published_quiz_is_an_empty_read_and_cannot_be_started()
    {
        await using var f = await Fixture.Create(); f.NoQuiz = true;
        var response = await f.Browser.Send(f.Root); Assert.Equal(200, response.Status);
        Assert.Equal(JsonValueKind.Null, Json(response).GetProperty("quiz").ValueKind);
        Assert.Equal(404, (await f.Start()).Status); Assert.Equal(0, f.Mutations);
    }
    [Fact]
    public async Task Attempt_limit_conflict_is_sanitized_and_not_retried()
    {
        await using var f = await Fixture.Create(); f.MutationStatus = 409;
        var response = await f.Start(); Assert.Equal(409, response.Status); Assert.DoesNotContain("PRIVATE", response.Body); Assert.Equal(1, f.Mutations);
    }
}
