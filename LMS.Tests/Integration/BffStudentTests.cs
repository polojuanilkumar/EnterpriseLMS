using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;

[Trait("Category", "HostIntegration")]
public sealed class BffStudentTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api = new();
        public WebApplication App = null!; public InProcessServer Server = null!; public Browser Browser = null!;
        public readonly Guid Available = Guid.NewGuid(), Completed = Guid.NewGuid(), Cancelled = Guid.NewGuid(), Missing = Guid.NewGuid();
        public CourseView[] Courses = []; public LearnerEnrollment[] Enrollments = [];
        public readonly List<string> Reads = []; public int Mutations; public int? MutationStatus;
        public bool AmbiguousCommit, ForeignOwner;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Courses = [new(f.Api.Course, "Active <script>", "Description", true, CategoryName: "Science", Level: "Beginner", DurationInMinutes: 90),
                new(f.Available, "Available", "", true), new(f.Completed, "Completed", "", true), new(f.Cancelled, "Cancelled", "", true),
                new(Guid.NewGuid(), "Draft must stay hidden", "", false, Status: "Draft")];
            f.Enrollments = [f.Record(f.Api.Course, 1), f.Record(f.Completed, 2), f.Record(f.Cancelled, 3), f.Record(f.Missing, 1)];
            f.Api.StudentResponse = f.Respond;
            (f.App, f.Server) = await InProcessMvcTests.Host(f.Api); f.Browser = new(f.Server);
            // Deliberately stale cached role: stored API context must decide access.
            f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App, f.Api, "Admin")); return f;
        }
        private LearnerEnrollment Record(Guid course, int status) => new(Guid.NewGuid(), course, status, DateTime.UtcNow, status == 2 ? DateTime.UtcNow : null, 0);
        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/Courses") && !path.StartsWith("/api/Enrollments")) return null;
            Assert.Equal(Api.Organization.ToString(), Assert.Single(request.Headers.GetValues("X-Organization-Id")));
            Assert.Equal(Api.AccessToken, request.Headers.Authorization!.Parameter);
            if (request.Method == HttpMethod.Get)
            {
                Reads.Add(path);
                if (path == "/api/Courses") return Envelope(Courses);
                if (path == "/api/Enrollments/me") return Envelope(Enrollments);
                if (Guid.TryParse(path.Split('/').Last(), out var id) && Enrollments.SingleOrDefault(e => e.Id == id) is { } enrollment)
                    return Envelope(enrollment with { UserId = ForeignOwner ? Guid.NewGuid() : Api.User });
                throw new InvalidOperationException("Unexpected content/progress request");
            }
            Mutations++;
            if (MutationStatus is int status) return new((HttpStatusCode)status) { Content = JsonContent.Create(new { message = "private upstream <script>diagnostic</script>" }) };
            if (path.EndsWith("/enroll"))
            {
                var course = Guid.Parse(path.Split('/')[3]); var existing = Enrollments.SingleOrDefault(e => e.CourseId == course);
                if (existing is { Status: 1 or 2 }) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "private duplicate" }) };
                Enrollments = existing is null ? [.. Enrollments, Record(course, 1)] : Enrollments.Select(e => e.Id == existing.Id ? e with { Status = 1 } : e).ToArray();
            }
            else if (path.EndsWith("/cancel"))
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                var id = Guid.Parse(path.Split('/')[3]); var existing = Enrollments.Single(e => e.Id == id);
                if (existing.Status != 1) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "private conflict" }) };
                Enrollments = Enrollments.Select(e => e.Id == id ? e with { Status = 3 } : e).ToArray();
            }
            else throw new InvalidOperationException("Unexpected mutation");
            if (AmbiguousCommit) throw new HttpRequestException("private transport diagnostic");
            return Envelope(new { });
        }
        public async Task<InProcessServer.Response> Enroll(Guid course, bool confirmed = false)
            => await Browser.Send($"/bff/student/courses/{course}/enroll", "POST", new { confirmedReenrollment = confirmed }, await Browser.Csrf());
        public async Task<InProcessServer.Response> Cancel(LearnerEnrollment enrollment, bool confirmed = true)
            => await Browser.Send($"/bff/student/courses/{enrollment.CourseId}/enrollments/{enrollment.Id}/cancel", "POST", new { confirmed }, await Browser.Csrf());
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Api.Dispose(); }
    }
    [Fact]
    public async Task Catalog_and_my_courses_project_metadata_and_all_states_without_content_or_progress()
    {
        await using var f = await Fixture.Create();
        var catalog = await f.Browser.Send("/bff/student/courses"); Assert.Equal(200, catalog.Status);
        var rows = Json(catalog).GetProperty("courses").EnumerateArray().ToArray(); Assert.Equal(4, rows.Length);
        var available = rows.Single(r => r.GetProperty("courseId").GetGuid() == f.Available);
        Assert.Equal(JsonValueKind.Null, available.GetProperty("enrollment").ValueKind);
        var course = rows.Single(r => r.GetProperty("courseId").GetGuid() == f.Api.Course).GetProperty("course");
        Assert.Equal("Science", course.GetProperty("categoryName").GetString()); Assert.Equal(90, course.GetProperty("durationInMinutes").GetInt32());
        var mine = await f.Browser.Send("/bff/student/my-courses"); Assert.Equal(200, mine.Status);
        var own = Json(mine).GetProperty("courses").EnumerateArray().ToArray(); Assert.Equal(4, own.Length);
        Assert.DoesNotContain(own, r => r.GetProperty("courseId").GetGuid() == f.Available);
        Assert.Equal(JsonValueKind.Null, own.Single(r => r.GetProperty("courseId").GetGuid() == f.Missing).GetProperty("course").ValueKind);
        Assert.Equal(new[] { "Active", "Cancelled", "Completed" }, own.Select(r => r.GetProperty("enrollment").GetProperty("status").GetString()).Distinct().Order().ToArray());
        Assert.All(f.Reads, path => Assert.Contains(path, new[] { "/api/Courses", "/api/Enrollments/me" }));
        Assert.DoesNotContain("progressPercentage", mine.Body); Assert.DoesNotContain(f.Api.AccessToken, mine.Body);
    }
    [Fact]
    public async Task Enrollment_reenrollment_confirmation_conflicts_and_cancellation_reuse_existing_operations()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(204, (await f.Enroll(f.Available)).Status);
        var duplicate = await f.Enroll(f.Available); Assert.Equal(409, duplicate.Status); Assert.Equal("conflict", Json(duplicate).GetProperty("code").GetString());
        var cancelled = f.Enrollments.Single(e => e.CourseId == f.Cancelled); var before = f.Mutations;
        Assert.Equal(400, (await f.Enroll(f.Cancelled)).Status); Assert.Equal(before, f.Mutations);
        Assert.Equal(204, (await f.Enroll(f.Cancelled, true)).Status); Assert.Equal(cancelled.Id, f.Enrollments.Single(e => e.CourseId == f.Cancelled).Id);
        before = f.Mutations; Assert.Equal(400, (await f.Cancel(cancelled, false)).Status); Assert.Equal(before, f.Mutations);
        Assert.Equal(204, (await f.Cancel(cancelled)).Status); Assert.Equal(3, f.Enrollments.Single(e => e.Id == cancelled.Id).Status);
        Assert.Equal(409, (await f.Cancel(f.Enrollments.Single(e => e.CourseId == f.Completed))).Status);
    }
    [Theory]
    [InlineData("Instructor", false)] [InlineData("Admin", false)] [InlineData("Student", true)] [InlineData(null, true)]
    public async Task Stored_nonstudent_access_is_denied_even_with_cached_Student(string? role, bool platform)
    {
        await using var f = await Fixture.Create(); f.Api.CurrentRole = role!; f.Api.Platform = platform;
        f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App, f.Api, "Student"));
        Assert.Equal(403, (await f.Browser.Send("/bff/student/courses")).Status);
        Assert.Equal(403, (await f.Browser.Send("/bff/student/my-courses")).Status);
        Assert.Equal(403, (await f.Enroll(f.Available)).Status); Assert.Empty(f.Reads); Assert.Equal(0, f.Mutations);
    }
    [Fact]
    public async Task Csrf_injected_identity_foreign_enrollments_and_organization_mismatches_are_rejected()
    {
        await using var f = await Fixture.Create();
        var path = $"/bff/student/courses/{f.Available}/enroll";
        Assert.Equal(400, (await f.Browser.Send(path, "POST", new { })).Status);
        var injected = await f.Browser.Send(path, "POST", new { userId = Guid.NewGuid(), organizationId = Guid.NewGuid() }, await f.Browser.Csrf());
        Assert.Equal(400, injected.Status); Assert.Equal("validation_failed", Json(injected).GetProperty("code").GetString());
        var csrf = await f.Browser.Csrf();
        var ignoredHeader = await f.Server.Send("/bff/student/courses", cookie: f.Browser.Cookie, organizationHeader: Guid.NewGuid().ToString()); Assert.Equal(200, ignoredHeader.Status);
        f.ForeignOwner = true; Assert.Equal(403, (await f.Cancel(f.Enrollments[0])).Status);
        f.ForeignOwner = false; Assert.Equal(404, (await f.Enroll(Guid.NewGuid())).Status);
        Assert.Equal(404, (await f.Browser.Send($"/bff/student/courses/{f.Api.Course}/enrollments/{Guid.NewGuid()}/cancel", "POST", new { confirmed = true }, csrf)).Status);
        f.Api.ContextMismatch = true; Assert.Equal(403, (await f.Browser.Send("/bff/student/my-courses")).Status); Assert.Equal(0, f.Mutations);
    }
    [Theory]
    [InlineData(400)] [InlineData(409)]
    public async Task Mutation_errors_are_sanitized_with_correct_status_and_no_replay(int status)
    {
        await using var f = await Fixture.Create(); f.MutationStatus = status;
        var response = await f.Enroll(f.Available); Assert.Equal(status, response.Status); Json(response);
        Assert.DoesNotContain("private", response.Body); Assert.DoesNotContain("script", response.Body); Assert.Equal(1, f.Mutations);
    }
    [Fact]
    public async Task Ambiguous_commit_can_be_read_back_without_replaying_enrollment()
    {
        await using var f = await Fixture.Create(); f.AmbiguousCommit = true;
        var response = await f.Enroll(f.Available); Assert.Equal(502, response.Status); Json(response); Assert.Equal(1, f.Mutations);
        var stored = await f.Browser.Send("/bff/student/my-courses"); Assert.Equal(200, stored.Status);
        Assert.Contains(Json(stored).GetProperty("courses").EnumerateArray(), row => row.GetProperty("courseId").GetGuid() == f.Available);
        Assert.Equal(1, f.Mutations);
    }
    [Fact]
    public async Task Unselected_or_anonymous_accounts_and_unconfirmed_or_csrf_less_cancellation_are_rejected()
    {
        await using var f = await Fixture.Create();
        var anonymous = await f.Server.Send("/bff/student/courses"); Assert.Equal(401, anonymous.Status); Json(anonymous);
        var cancellation = f.Enrollments[0];
        var noCsrf = await f.Browser.Send($"/bff/student/courses/{cancellation.CourseId}/enrollments/{cancellation.Id}/cancel", "POST", new { confirmed = true });
        Assert.Equal(400, noCsrf.Status); Assert.Equal("csrf_validation_failed", Json(noCsrf).GetProperty("code").GetString());
        var unpublished = f.Courses.Single(c => !c.IsPublished);
        Assert.Equal(404, (await f.Enroll(unpublished.Id)).Status); Assert.Equal(0, f.Mutations);
        var login = await f.Browser.Send("/bff/login", "POST", new { email = "student@example.test", password = "synthetic-password" }, await f.Browser.Csrf());
        Assert.Equal(200, login.Status);
        var noSelection = await f.Browser.Send("/bff/student/my-courses"); Assert.Equal(403, noSelection.Status); Json(noSelection);
    }}
