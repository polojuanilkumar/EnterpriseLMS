using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;

public sealed class BffStudentProgressTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api = new();
        public WebApplication App = null!; public InProcessServer Server = null!; public Browser Browser = null!;
        public readonly Guid Enrollment = Guid.NewGuid();
        public readonly DateTime Timestamp = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        public int State = 1, Total = 2, Completed = 1, Mutations;
        public bool Started, LessonComplete, ForeignOwner, ForeignProgress, WrongProgressLesson, WrongSection, WrongLesson, Draft, Ambiguous, WrongEnrollmentCourse;
        public int? MutationStatus;
        public decimal Percentage;
        public DateTime LastAccessed;
        public readonly List<(string Path, string Method, string Body)> Writes = [];
        public readonly List<string> Reads = [];
        public string Course => $"/bff/student/courses/{Api.Course}";
        public string Lesson => Course + $"/sections/{Api.Section}/lessons/{Api.Lesson}";
        public string Complete => Course + $"/enrollments/{Enrollment}/complete";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.LastAccessed = f.Timestamp;
            f.Api.StudentResponse = f.Respond;
            (f.App, f.Server) = await InProcessMvcTests.Host(f.Api); f.Browser = new(f.Server);
            f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App, f.Api, "Admin"));
            return f;
        }
        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal(Api.Organization.ToString(), Assert.Single(request.Headers.GetValues("X-Organization-Id")));
            Assert.Equal(Api.AccessToken, request.Headers.Authorization!.Parameter);
            if (request.Method != HttpMethod.Get)
            {
                Mutations++; var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); Writes.Add((path, request.Method.Method, body));
                if (MutationStatus is int status) return new((HttpStatusCode)status) { Content = JsonContent.Create(new { message = "PRIVATE upstream diagnostic" }) };
                if (State != 1) return new(HttpStatusCode.Forbidden);
                if (path == $"/api/Enrollments/{Enrollment}/complete")
                {
                    Assert.Equal(HttpMethod.Patch, request.Method);
                    if (Total == 0 || Completed != Total) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "PRIVATE prerequisite failure" }) };
                    State = 2;
                }
                else
                {
                    if (path.EndsWith("/start")) { Assert.Equal(HttpMethod.Post, request.Method); Started = true; }
                    else if (!Started) return new(HttpStatusCode.NotFound);
                    else if (path.EndsWith("/progress")) { Assert.Equal(HttpMethod.Patch, request.Method); Percentage = JsonDocument.Parse(body).RootElement.GetProperty("progressPercentage").GetDecimal(); if (Percentage == 100) LessonComplete = true; }
                    else if (path.EndsWith("/complete")) { Assert.Equal(HttpMethod.Patch, request.Method); Percentage = 100; LessonComplete = true; }
                    else throw new InvalidOperationException("Unexpected mutation");
                    LastAccessed = Timestamp.AddDays(1);
                }
                if (Ambiguous) throw new HttpRequestException("PRIVATE committed transport failure");
                return Envelope(new { });
            }
            Reads.Add(path);
            if (path == $"/api/Courses/{Api.Course}") return Envelope(new CourseView(Api.Course, "Course", "Description", !Draft));
            if (path == "/api/Enrollments/me") return Envelope(new[] { Record() with { CourseId = Api.Course } });
            if (path == $"/api/Enrollments/{Enrollment}") return Envelope(Record());
            if (path == $"/api/Courses/{Api.Course}/sections") return Envelope(new[] { new SectionView(Api.Section, WrongSection ? Guid.NewGuid() : Api.Course, "Section", 1) });
            if (path == $"/api/Sections/{Api.Section}/lessons") return Envelope(new[] { new LessonView(Api.Lesson, WrongLesson ? Guid.NewGuid() : Api.Section, "Lesson", null, true, 1) });
            if (path == $"/api/Lessons/{Api.Lesson}") return Envelope(new LessonView(Api.Lesson, Api.Section, "Lesson", null, true, 1));
            if (path == $"/api/Courses/{Api.Course}/progress") return Envelope(new CurrentCourseProgress(Api.Course, Api.User, Total, Completed, Total == 0 ? 0 : (decimal)Completed / Total * 100, Total > 0 && Total == Completed, State, State == 2 ? Timestamp : null));
            if (path == $"/api/Lessons/{Api.Lesson}/progress") return Started
                ? Envelope(new LessonProgressView(Guid.NewGuid(), ForeignProgress ? Guid.NewGuid() : Api.User, WrongProgressLesson ? Guid.NewGuid() : Api.Lesson, LessonComplete, LessonComplete ? Timestamp : null, Timestamp, Percentage, LastAccessed))
                : new(HttpStatusCode.NotFound);
            throw new InvalidOperationException("Unexpected progress upstream: " + path);
        }
        private LearnerEnrollment Record() => new(Enrollment, WrongEnrollmentCourse ? Guid.NewGuid() : Api.Course, State, Timestamp, State == 2 ? Timestamp : null, 0, ForeignOwner ? Guid.NewGuid() : Api.User);
        public async Task<InProcessServer.Response> Change(string path, object body, string method = "POST") => await Browser.Send(path, method, body, await Browser.Csrf());
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Api.Dispose(); }
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Reads_keep_current_and_historical_state_separate_and_do_not_access_or_save_progress(int state)
    {
        await using var f = await Fixture.Create(); f.State = state; f.Started = true;
        foreach (var path in new[] { f.Course + "/progress", f.Lesson + "/progress" })
        {
            var response = await f.Browser.Send(path); Assert.Equal(200, response.Status);
            var json = Json(response); Assert.Equal(50, json.GetProperty("current").GetProperty("progressPercentage").GetDecimal());
            Assert.False(json.GetProperty("current").GetProperty("isCompleted").GetBoolean());
            Assert.Equal(state == 2 ? "Completed" : "Active", json.GetProperty("enrollment").GetProperty("status").GetString());
            Assert.Equal(state == 2 ? JsonValueKind.String : JsonValueKind.Null, json.GetProperty("enrollment").GetProperty("completedAt").ValueKind);
            Assert.DoesNotContain("userId", response.Body); Assert.DoesNotContain(f.Api.AccessToken, response.Body); Assert.Contains("no-store", response.Headers.CacheControl.ToString());
        }
        Assert.Equal(f.Timestamp, f.LastAccessed); Assert.Equal(0, f.Mutations);
        Assert.DoesNotContain(f.Reads, x => x.Contains("quiz") || x.Contains("content-items") || x.Contains("assignment"));
    }
    [Fact]
    public async Task Explicit_start_update_and_complete_use_existing_API_methods_and_payloads()
    {
        await using var f = await Fixture.Create();
        var before = await f.Browser.Send(f.Lesson + "/progress"); Assert.Equal(JsonValueKind.Null, Json(before).GetProperty("lesson").ValueKind);
        Assert.Equal(204, (await f.Change(f.Lesson + "/progress/start", new { })).Status);
        Assert.Equal(204, (await f.Change(f.Lesson + "/progress", new { progressPercentage = 62.5m }, "PATCH")).Status);
        Assert.Equal(62.5m, f.Percentage); Assert.Equal(204, (await f.Change(f.Lesson + "/progress/complete", new { })).Status);
        Assert.Equal(new[] { "POST", "PATCH", "PATCH" }, f.Writes.Select(x => x.Method));
        Assert.Equal("{}", f.Writes[0].Body); Assert.Equal(62.5m, JsonDocument.Parse(f.Writes[1].Body).RootElement.GetProperty("progressPercentage").GetDecimal());
        Assert.True(f.LessonComplete); Assert.Equal(100, f.Percentage);
        var after = await f.Browser.Send(f.Lesson + "/progress"); Assert.True(Json(after).GetProperty("lesson").GetProperty("isCompleted").GetBoolean());
    }
    [Fact]
    public async Task MVC_mark_complete_requires_CSRF_and_server_confirmation_without_changing_BFF_payload()
    {
        await using var f=await Fixture.Create();f.Started=true;f.Percentage=37.5m;
        f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App,f.Api,"Student"));
        var path=$"/learn/{f.Api.Organization}/courses/{f.Api.Course}/sections/{f.Api.Section}/units/{f.Api.Lesson}/progress/complete";
        Assert.Equal(400,(await f.Server.Send(path,"POST",cookie:f.Browser.Cookie,form:"Form.Confirmed=true")).Status);
        var csrf=await f.Browser.Csrf();
        foreach(var form in new[]{"","Form.Confirmed=false","Form.Confirmed=not-a-boolean"})
            Assert.Equal(302,(await f.Server.Send(path,"POST",cookie:f.Browser.Cookie,form:form,csrf:csrf)).Status);
        Assert.Equal(0,f.Mutations);Assert.False(f.LessonComplete);Assert.Equal(37.5m,f.Percentage);Assert.Equal(f.Timestamp,f.LastAccessed);
        Assert.Equal(302,(await f.Server.Send(path,"POST",cookie:f.Browser.Cookie,form:"Form.Confirmed=true",csrf:csrf)).Status);
        var write=Assert.Single(f.Writes);Assert.Equal("PATCH",write.Method);Assert.Equal($"/api/Lessons/{f.Api.Lesson}/complete",write.Path);Assert.Equal("{}",write.Body);
        Assert.True(f.LessonComplete);Assert.Equal(100,f.Percentage);Assert.Equal(1,f.State);
    }
    [Fact]
    public async Task Saving_100_completes_lesson_without_completing_enrollment()
    {
        await using var f = await Fixture.Create(); f.Started = true;
        Assert.Equal(204, (await f.Change(f.Lesson + "/progress", new { progressPercentage = 100 }, "PATCH")).Status);
        Assert.True(f.LessonComplete); Assert.Equal(1, f.State); Assert.Single(f.Writes);
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(2, 1)]
    public async Task Zero_or_unfinished_published_lessons_conflict_at_guarded_API_without_new_BFF_rules(int total, int completed)
    {
        await using var f = await Fixture.Create(); f.Total = total; f.Completed = completed;
        var response = await f.Change(f.Complete, new { confirmed = true }); Assert.Equal(409, response.Status);
        Assert.Equal(1, f.Mutations); Assert.Equal(1, f.State); Assert.DoesNotContain("PRIVATE", response.Body);
    }
    [Fact]
    public async Task Confirmed_completion_records_history_and_repeat_conflicts_without_replay()
    {
        await using var f = await Fixture.Create(); f.Completed = f.Total;
        Assert.Equal(400, (await f.Change(f.Complete, new { confirmed = false })).Status); Assert.Equal(0, f.Mutations);
        Assert.Equal(204, (await f.Change(f.Complete, new { confirmed = true })).Status); Assert.Equal(2, f.State);
        Assert.Equal(409, (await f.Change(f.Complete, new { confirmed = true })).Status); Assert.Equal(1, f.Mutations);
        f.Total = 3; f.Completed = 1; var read = await f.Browser.Send(f.Course + "/progress");
        Assert.Equal("Completed", Json(read).GetProperty("enrollment").GetProperty("status").GetString()); Assert.False(Json(read).GetProperty("current").GetProperty("isCompleted").GetBoolean());
    }
    [Fact]
    public async Task Completed_enrollments_allow_reads_but_block_all_learner_mutations()
    {
        await using var f = await Fixture.Create(); f.State = 2; f.Started = true;
        Assert.Equal(200, (await f.Browser.Send(f.Lesson + "/progress")).Status);
        Assert.Equal(409, (await f.Change(f.Lesson + "/progress/start", new { })).Status);
        Assert.Equal(409, (await f.Change(f.Lesson + "/progress", new { progressPercentage = 50 }, "PATCH")).Status);
        Assert.Equal(409, (await f.Change(f.Lesson + "/progress/complete", new { })).Status); Assert.Equal(0, f.Mutations);
    }
    [Theory]
    [InlineData("owner", 403)] [InlineData("role", 403)] [InlineData("organization", 403)] [InlineData("section", 404)]
    [InlineData("lesson", 404)] [InlineData("enrollment", 404)] [InlineData("progress-owner", 403)] [InlineData("progress-lesson", 404)] [InlineData("draft", 404)] [InlineData("cancelled", 403)]
    public async Task Verified_Student_scope_and_stored_relationships_gate_reads_and_mutations(string denial, int status)
    {
        await using var f = await Fixture.Create(); f.Started = true;
        f.ForeignOwner = denial == "owner"; if (denial == "role") f.Api.CurrentRole = "Instructor"; f.Api.ContextMismatch = denial == "organization";
        f.WrongSection = denial == "section"; f.WrongLesson = denial == "lesson"; f.WrongEnrollmentCourse = denial == "enrollment";
        f.ForeignProgress = denial == "progress-owner"; f.WrongProgressLesson = denial == "progress-lesson"; f.Draft = denial == "draft"; if (denial == "cancelled") f.State = 3;
        Assert.Equal(status, (await f.Browser.Send(f.Lesson + "/progress")).Status);
        Assert.Equal(status, (await f.Change(f.Lesson + "/progress/start", new { })).Status); Assert.Equal(0, f.Mutations);
    }
    [Fact]
    public async Task Anonymous_CSRF_injected_identity_bad_percentages_and_foreign_enrollment_ID_are_rejected()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(401, (await f.Server.Send(f.Course + "/progress")).Status);
        Assert.Equal(400, (await f.Browser.Send(f.Lesson + "/progress/start", "POST", new { })).Status);
        Assert.Equal(400, (await f.Change(f.Lesson + "/progress/start", new { userId = Guid.NewGuid() })).Status);
        Assert.Equal(400, (await f.Change(f.Complete, new { confirmed = true, organizationId = Guid.NewGuid() })).Status);
        foreach (var body in new object[] { new { }, new { progressPercentage = -1 }, new { progressPercentage = 101 }, new { progressPercentage = 50, userId = Guid.NewGuid() } })
            Assert.Equal(400, (await f.Change(f.Lesson + "/progress", body, "PATCH")).Status);
        Assert.Equal(404, (await f.Change(f.Complete.Replace(f.Enrollment.ToString(), Guid.NewGuid().ToString()), new { confirmed = true })).Status);
        Assert.Equal(0, f.Mutations);
        Assert.Equal(200, (await f.Server.Send(f.Course + "/progress", cookie: f.Browser.Cookie, organizationHeader: Guid.NewGuid().ToString())).Status);
    }
    [Fact]
    public async Task Ambiguous_commit_can_be_read_back_without_replaying_write()
    {
        await using var f = await Fixture.Create(); f.Ambiguous = true;
        var response = await f.Change(f.Lesson + "/progress/start", new { }); Assert.Equal(502, response.Status); Assert.DoesNotContain("PRIVATE", response.Body);
        var read = await f.Browser.Send(f.Lesson + "/progress"); Assert.Equal(200, read.Status); Assert.Equal(JsonValueKind.Object, Json(read).GetProperty("lesson").ValueKind); Assert.Equal(1, f.Mutations);
    }
    [Theory]
    [InlineData(400)] [InlineData(409)]
    public async Task Guarded_API_validation_and_conflicts_remain_sanitized(int status)
    {
        await using var f = await Fixture.Create(); f.MutationStatus = status;
        var response = await f.Change(f.Lesson + "/progress/start", new { }); Assert.Equal(status, response.Status); Assert.DoesNotContain("PRIVATE", response.Body); Assert.Equal(1, f.Mutations);
    }
}
