using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
namespace LMS.Tests.Integration;

// Actual cookie/MVC/BFF pipeline, fake scoped API transport; no database or sockets.
public sealed class BffStudentViewerTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api = new();
        public WebApplication App = null!; public InProcessServer Server = null!; public string Cookie = "";
        public readonly Guid Enrollment = Guid.NewGuid();
        public int State = 1, MediaStatus = 200, MediaType = 5, MediaRequests;
        public bool DraftCourse, DraftLesson, ForeignOwner, WrongCourse, WrongSection, WrongLesson, WrongItem, LegacyOnly, BadMime, MediaDenied;
        public string LegacyVideo = "https://youtu.be/dQw4w9WgXcQ";
        public string YouTubeId = "dQw4w9WgXcQ";
        public readonly List<string> Reads = [];
        public string Root => $"/bff/student/courses/{Api.Course}";
        public string Section => Root + $"/sections/{Api.Section}";
        public string Unit => Section + $"/lessons/{Api.Lesson}";
        public string Media => Unit + $"/items/{Api.Item}/media";
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); f.Api.StudentResponse = f.Respond;
            (f.App, f.Server) = await InProcessMvcTests.Host(f.Api);
            f.Cookie = InProcessMvcTests.AuthCookie(f.App, f.Api, "Admin"); // stored role is authoritative
            return f;
        }
        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            Reads.Add(path);
            Assert.True(HttpMethod.Get == request.Method || HttpMethod.Head == request.Method);
            Assert.Equal(Api.Organization.ToString(), Assert.Single(request.Headers.GetValues("X-Organization-Id")));
            Assert.Equal(Api.AccessToken, request.Headers.Authorization!.Parameter);
            var record = new LearnerEnrollment(Enrollment, Api.Course, State, DateTime.UtcNow, null, 0, ForeignOwner ? Guid.NewGuid() : Api.User);
            if (path == "/api/Enrollments/me") return Envelope(new[] { record });
            if (path == $"/api/Enrollments/{Enrollment}") return Envelope(record);
            if (path == $"/api/Courses/{Api.Course}") return Envelope(new CourseView(WrongCourse ? Guid.NewGuid() : Api.Course, "Course", "Description", !DraftCourse));
            if (path == $"/api/Courses/{Api.Course}/sections") return Envelope(new[] {
                new SectionView(Guid.NewGuid(), Api.Course, "Second", 2), new SectionView(Api.Section, WrongSection ? Guid.NewGuid() : Api.Course, "First", 1) });
            if (path == $"/api/Sections/{Api.Section}/lessons") return Envelope(new[] {
                new LessonView(Guid.NewGuid(), Api.Section, "Draft hidden", null, false, 0),
                new LessonView(Guid.NewGuid(), Api.Section, "Second lesson", null, true, 2),
                new LessonView(Api.Lesson, WrongLesson ? Guid.NewGuid() : Api.Section, "First lesson", null, !DraftLesson, 1) });
            if (path == $"/api/Lessons/{Api.Lesson}") return Envelope(new LessonView(Api.Lesson, WrongLesson ? Guid.NewGuid() : Api.Section, "Lesson", "Description", !DraftLesson, 1));
            if (path == $"/api/Lessons/{Api.Lesson}/content-items")
            {
                ItemView[] items = LegacyOnly ? [] : Enumerable.Range(1, 5).Reverse().Select(type => new ItemView(type == MediaType ? Api.Item : Guid.NewGuid(), WrongItem ? Guid.NewGuid() : Api.Lesson,
                    type, "Type " + type, type, true, type == 1 ? "<script>plain text</script>" : null, type == 2 ? YouTubeId : null, type >= 3 ? Guid.NewGuid() : null)).ToArray();
                return Envelope(new ContentView(items, new LegacyView("Legacy <img>", LegacyOnly ? LegacyVideo : "https://evil.test/video")));
            }
            if (path == $"/api/Lessons/{Api.Lesson}/content-items/{Api.Item}/media")
            {
                MediaRequests++;
                if (MediaDenied) return new(HttpStatusCode.Forbidden);
                if (request.Headers.Range is not null) Assert.Equal("bytes=2-4", request.Headers.Range.ToString());
                if (request.Headers.IfRange is not null) Assert.Equal("\"version\"", request.Headers.IfRange.ToString());
                var response = new HttpResponseMessage((HttpStatusCode)MediaStatus) { Content = new ByteArrayContent(MediaStatus == 416 ? [] : [1, 2, 3]) };
                response.Headers.AcceptRanges.Add("bytes");
                response.Content.Headers.ContentType = new(BadMime ? "text/html" : MediaType switch { 3 => "audio/mpeg", 4 => "video/mp4", _ => "application/pdf" });
                if (MediaStatus == 206) response.Content.Headers.ContentRange = new(2, 4, 10);
                if (MediaStatus == 416) response.Content.Headers.ContentRange = new(10);
                response.Content.Headers.ContentDisposition = new("attachment") { FileName = "untrusted-name.html" };
                return response;
            }
            throw new InvalidOperationException("Unexpected viewer upstream read: " + path);
        }
        public Task<InProcessServer.Response> Get(string path, string method = "GET", string? range = null)
            => Server.Send(path, method, Cookie, range: range, ifRange: range is null ? null : "\"version\"");
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Api.Dispose(); }
    }
    private static JsonElement Json(InProcessServer.Response response) => JsonDocument.Parse(response.Body).RootElement;
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Active_and_completed_reads_return_ordered_published_resources_and_safe_five_types(int state)
    {
        await using var f = await Fixture.Create(); f.State = state;
        foreach (var path in new[] { f.Root, f.Root + "/sections" })
        {
            var response = await f.Get(path); Assert.Equal(200, response.Status);
            Assert.Equal(new[] { 1, 2 }, Json(response).GetProperty("sections").EnumerateArray().Select(x => x.GetProperty("displayOrder").GetInt32()));
        }
        foreach (var path in new[] { f.Section, f.Section + "/lessons" })
        {
            var response = await f.Get(path); Assert.Equal(200, response.Status);
            Assert.Equal(new[] { 1, 2 }, Json(response).GetProperty("lessons").EnumerateArray().Select(x => x.GetProperty("displayOrder").GetInt32()));
            Assert.DoesNotContain("Draft hidden", response.Body);
        }
        foreach (var path in new[] { f.Unit, f.Unit + "/content-items" })
        {
            var response = await f.Get(path); Assert.Equal(200, response.Status);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Json(response).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("type").GetInt32()));
            Assert.Equal(JsonValueKind.Null, Json(response).GetProperty("legacy").ValueKind);
            Assert.DoesNotContain("assetId", response.Body); Assert.DoesNotContain("videoUrl", response.Body); Assert.DoesNotContain(f.Api.AccessToken, response.Body);
        }
        Assert.DoesNotContain(f.Reads, x => x.Contains("progress") || x.Contains("quiz") || x.Contains("assignment"));
    }
    [Fact]
    public async Task Legacy_content_is_returned_only_without_items_and_video_is_normalized_to_an_ID()
    {
        await using var f = await Fixture.Create(); f.LegacyOnly = true;
        var response = await f.Get(f.Unit); Assert.Equal(200, response.Status);
        Assert.Empty(Json(response).GetProperty("items").EnumerateArray());
        Assert.Equal("dQw4w9WgXcQ", Json(response).GetProperty("legacy").GetProperty("youTubeVideoId").GetString());
        Assert.DoesNotContain("https://", response.Body);
    }
    [Theory]
    [InlineData("https://evil.test/watch?v=dQw4w9WgXcQ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://youtube.com.evil.test/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ%0a")]
    public async Task Unsafe_legacy_URLs_and_invalid_item_video_IDs_are_not_projected(string url)
    {
        await using var f = await Fixture.Create(); f.LegacyOnly = true; f.LegacyVideo = url;
        var response = await f.Get(f.Unit); Assert.Equal(200, response.Status);
        Assert.Equal(JsonValueKind.Null, Json(response).GetProperty("legacy").GetProperty("youTubeVideoId").ValueKind);
        Assert.DoesNotContain(url, response.Body);
        f.LegacyOnly = false; f.YouTubeId = url;
        response = await f.Get(f.Unit); Assert.Equal(200, response.Status);
        Assert.Equal(JsonValueKind.Null, Json(response).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("type").GetInt32() == 2).GetProperty("youTubeVideoId").ValueKind);
    }
    [Theory]
    [InlineData("course")] [InlineData("section")] [InlineData("lesson")] [InlineData("item")]
    public async Task Every_stored_parent_relationship_is_checked_before_media(string mismatch)
    {
        await using var f = await Fixture.Create();
        f.WrongCourse = mismatch == "course"; f.WrongSection = mismatch == "section"; f.WrongLesson = mismatch == "lesson"; f.WrongItem = mismatch == "item";
        Assert.Equal(404, (await f.Get(f.Media)).Status); Assert.Equal(0, f.MediaRequests);
    }
    [Theory]
    [InlineData("section")] [InlineData("lesson")] [InlineData("item")]
    public async Task Guessed_nested_IDs_do_not_reach_media(string nested)
    {
        await using var f = await Fixture.Create(); var path = f.Media.Replace((nested == "section" ? f.Api.Section : nested == "lesson" ? f.Api.Lesson : f.Api.Item).ToString(), Guid.NewGuid().ToString());
        Assert.Equal(404, (await f.Get(path)).Status); Assert.Equal(0, f.MediaRequests);
    }
    [Theory]
    [InlineData("course", 404)] [InlineData("lesson", 404)] [InlineData("cancelled", 403)] [InlineData("owner", 403)] [InlineData("organization", 403)] [InlineData("role", 403)]
    public async Task Publication_enrollment_ownership_and_cross_organization_denials_hide_content(string denial, int status)
    {
        await using var f = await Fixture.Create();
        f.DraftCourse = denial == "course"; f.DraftLesson = denial == "lesson"; f.State = denial == "cancelled" ? 3 : 1;
        f.ForeignOwner = denial == "owner"; f.Api.ContextMismatch = denial == "organization"; if (denial == "role") f.Api.CurrentRole = "Instructor";
        foreach (var method in new[] { "GET", "HEAD" }) { var response = await f.Get(f.Media, method); Assert.Equal(status, response.Status); Assert.DoesNotContain("Type 5", response.Body); }
        Assert.Equal(0, f.MediaRequests);
    }
    [Theory]
    [InlineData(3, 200)] [InlineData(3, 206)] [InlineData(3, 416)]
    [InlineData(4, 200)] [InlineData(4, 206)] [InlineData(4, 416)]
    [InlineData(5, 200)] [InlineData(5, 206)] [InlineData(5, 416)]
    public async Task Authenticated_GET_HEAD_and_ranges_preserve_safe_headers(int type, int status)
    {
        await using var f = await Fixture.Create(); f.MediaType = type; f.MediaStatus = status;
        foreach (var method in new[] { "GET", "HEAD" })
        {
            var response = await f.Get(f.Media + "?download=true", method, status == 206 ? "bytes=2-4" : null);
            Assert.Equal(status, response.Status); Assert.Equal(type switch { 3 => "audio/mpeg", 4 => "video/mp4", _ => "application/pdf" }, response.Headers.ContentType.ToString());
            Assert.Contains("no-store", response.Headers.CacheControl.ToString()); Assert.Equal("nosniff", response.Headers["X-Content-Type-Options"].ToString());
            Assert.Equal("bytes", response.Headers.AcceptRanges.ToString()); Assert.DoesNotContain("untrusted", response.Headers.ContentDisposition.ToString());
            Assert.DoesNotContain(f.Api.AccessToken, response.Headers.ToString());
            if (status == 206) Assert.Equal("bytes 2-4/10", response.Headers.ContentRange.ToString());
            if (status == 416) Assert.Equal("bytes */10", response.Headers.ContentRange.ToString());
            if (method == "HEAD") Assert.Empty(response.Body);
            if (type == 5) { Assert.Contains("attachment", response.Headers.ContentDisposition.ToString()); Assert.Contains("sandbox;", response.Headers["Content-Security-Policy"].ToString()); }
        }
    }
    [Fact]
    public async Task Anonymous_revoked_upstream_denial_invalid_ranges_and_bad_MIME_are_safe()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(401, (await f.Server.Send(f.Media)).Status);
        Assert.Equal(404, (await f.Get(f.Media, range: "invalid")).Status); Assert.Equal(0, f.MediaRequests);
        f.BadMime = true; Assert.Equal(502, (await f.Get(f.Media)).Status); f.BadMime = false;
        f.MediaDenied = true; Assert.Equal(403, (await f.Get(f.Media)).Status);
        f.Api.Revoked = true; Assert.Equal(401, (await f.Get(f.Media)).Status);
    }
    [Fact]
    public async Task Browser_organization_header_cannot_override_server_selection()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(200, (await f.Server.Send(f.Unit, cookie: f.Cookie, organizationHeader: Guid.NewGuid().ToString())).Status);
        Assert.All(f.Api.OrganizationHeaders, x => Assert.Equal(f.Api.Organization.ToString(), x));
    }
}
