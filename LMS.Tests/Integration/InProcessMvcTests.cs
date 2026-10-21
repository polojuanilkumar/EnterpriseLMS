using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace LMS.Tests.Integration;

// Real MVC/authentication/antiforgery pipeline, fake API transport; no sockets, SQL or normal Program.
// This small IServer implementation uses the public ASP.NET server contract, avoiding new packages.
public sealed class InProcessServer : IServer
{
    public IFeatureCollection Features { get; } = new FeatureCollection();
    private Func<IFeatureCollection, Task>? dispatch;
    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
    {
        dispatch = async features =>
        {
            var context = application.CreateContext(features); Exception? error = null;
            try
            {
                await application.ProcessRequestAsync(context);
                if (features.Get<IHttpResponseFeature>() is ResponseFeature response) await response.Finish();
            }
            catch (Exception ex) { error = ex; throw; }
            finally { application.DisposeContext(context, error); }
        }; return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
    public sealed record Response(int Status, IHeaderDictionary Headers, string Body);
    public async Task<Response> Send(string path, string method = "GET", string? cookie = null, string? form = null, string? json = null, string? csrf = null, string? organizationHeader = null, string? range = null, string? ifRange = null, byte[]? rawBody = null, string? contentType = null)
    {
        if (dispatch is null) throw new InvalidOperationException("In-process server has not started.");
        var features = new FeatureCollection(); var bytes = rawBody ?? Encoding.UTF8.GetBytes(json ?? form ?? "");
        var request = new HttpRequestFeature { Scheme = "https", Protocol = "HTTP/1.1", Method = method,
            Path = path.Split('?')[0], QueryString = path.Contains('?') ? "?" + path.Split('?', 2)[1] : "",
            PathBase = "", Body = new MemoryStream(bytes), Headers = new HeaderDictionary() };
        request.Headers.Host = "web.example.invalid";
        if (cookie is not null) request.Headers.Cookie = cookie;
        if (form is not null) { request.Headers.ContentType = "application/x-www-form-urlencoded"; request.Headers.ContentLength = bytes.Length; }
        if (json is not null) { request.Headers.ContentType = "application/json"; request.Headers.ContentLength = bytes.Length; }
        if (contentType is not null) { request.Headers.ContentType = contentType; request.Headers.ContentLength = bytes.Length; }
        if (csrf is not null) request.Headers["X-CSRF-TOKEN"] = csrf;
        if (organizationHeader is not null) request.Headers["X-Organization-Id"] = organizationHeader;
        if (range is not null) request.Headers.Range = range;
        if (ifRange is not null) request.Headers.IfRange = ifRange;
        var response = new ResponseFeature(); var body = new MemoryStream();
        features.Set<IHttpRequestFeature>(request); features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature { RemoteIpAddress = IPAddress.Loopback });
        await dispatch(features);
        return new(response.StatusCode, response.Headers, Encoding.UTF8.GetString(body.ToArray()));
    }
    private sealed class ResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }
        private readonly Stack<(Func<object, Task> Callback, object State)> starting = new(), completed = new();
        public void OnStarting(Func<object, Task> callback, object state) => starting.Push((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) => completed.Push((callback, state));
        public async Task Finish()
        {
            while (starting.TryPop(out var callback)) await callback.Callback(callback.State);
            HasStarted = true;
            while (completed.TryPop(out var callback)) await callback.Callback(callback.State);
        }
    }
}

[Trait("Category", "HostIntegration")]
public sealed class InProcessMvcTests
{
    private sealed class HostStorage : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "LMS_Web_Integration_" + Guid.NewGuid().ToString("N"));
        public HostStorage() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("LMS_Web_Integration_", StringComparison.Ordinal)
                || (Directory.Exists(resolved) && new DirectoryInfo(resolved).Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new InvalidOperationException("Refusing unsafe isolated host cleanup.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    internal sealed class FakeApi : HttpMessageHandler
    {
        public Guid User = Guid.NewGuid(); public bool Revoked; public int Requests;
        public bool OrganizationDenied, Platform, SessionOutage, InactiveOrganization, ContextMismatch;
        public readonly List<string> OrganizationHeaders = [];
        public string CurrentRole = "Student";
        public HttpStatusCode LoginStatus = HttpStatusCode.OK;
        public string AccessToken = "synthetic-server-only-token";
        public Func<string, Task<bool>>? ValidateSession;
        public Func<HttpRequestMessage, HttpResponseMessage?>? StudentResponse;
        public readonly Guid Organization = Guid.NewGuid(), Course = Guid.NewGuid(), Section = Guid.NewGuid(), Lesson = Guid.NewGuid(), Item = Guid.NewGuid();
        private static HttpResponseMessage Envelope(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { Success = true, Data = value }) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (request.Headers.TryGetValues("X-Organization-Id", out var headers)) OrganizationHeaders.AddRange(headers);
            if (request.RequestUri!.AbsolutePath.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase))
            {
                if (LoginStatus != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(LoginStatus));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { Success = true,
                    Data = new ApiLogin { AccessToken = AccessToken, UserId = User, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) } }) });
            }
            if (request.RequestUri.AbsolutePath == "/api/account/session")
            {
                if (SessionOutage) throw new HttpRequestException("Synthetic transport failure");
                if (ValidateSession is not null) return Session(request.Headers.Authorization!.Parameter!);
                return Task.FromResult(new HttpResponseMessage(Revoked ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
            }
            if (request.RequestUri.AbsolutePath == "/api/organizations/me") return Task.FromResult(Envelope(new[] { new OrganizationView(Organization, "Test <organization>", !InactiveOrganization, "TEST") }));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/organizations/platform/context") return Task.FromResult(Platform
                ? Envelope(new { UserId = User, IsSuperAdmin = true }) : new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (path == $"/api/organizations/{Organization}/context") return Task.FromResult(OrganizationDenied
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Envelope(new OrganizationAccess(ContextMismatch ? Guid.NewGuid() : Organization, User, CurrentRole, Platform)));
            if (StudentResponse?.Invoke(request) is { } studentResponse) return Task.FromResult(studentResponse);
            if (path == $"/api/Courses/{Course}/progress") return Task.FromResult(Envelope(new { }));
            if (path == $"/api/Courses/{Course}") return Task.FromResult(Envelope(new CourseView(Course, "Synthetic course", "", true)));
            if (path == $"/api/Courses/{Course}/sections") return Task.FromResult(Envelope(new[] { new SectionView(Section, Course, "Section", 1) }));
            if (path == $"/api/Sections/{Section}/lessons") return Task.FromResult(Envelope(new[] { new LessonView(Lesson, Section, "Lesson", null, true, 1) }));
            if (path == $"/api/Lessons/{Lesson}/content-items") return Task.FromResult(Envelope(new ContentView([new ItemView(Item, Lesson, 5, "Synthetic PDF", 1, false, null, null, Guid.NewGuid())], null)));
            if (path == $"/api/Lessons/{Lesson}/content-items/{Item}/media")
            {
                Assert.Equal("Bearer synthetic-server-only-token", request.Headers.Authorization!.ToString());
                Assert.Equal(Organization.ToString(), Assert.Single(request.Headers.GetValues("X-Organization-Id")));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic-test-file")) };
                response.Content.Headers.ContentType = new("application/pdf"); return Task.FromResult(response);
            }
            throw new InvalidOperationException("Unexpected fake API request: " + request.RequestUri.AbsolutePath);
        }
        private async Task<HttpResponseMessage> Session(string jwt)
            => new(await ValidateSession!(jwt) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
    }
    internal static async Task<(WebApplication App, InProcessServer Server)> Host(FakeApi api, Action<WebApplication>? configure = null)
    {
        var storage = new HostStorage();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest",
            ApplicationName = typeof(AccountController).Assembly.FullName, ContentRootPath = storage.Root });
        builder.Configuration["LmsApi:BaseUrl"] = "https://api.example.invalid/";
        builder.Logging.ClearProviders();
        var server = new InProcessServer(); builder.Services.AddSingleton<IServer>(server);
        builder.Services.AddLearnerWeb(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(_ => storage);
        builder.Services.AddSingleton(new LearnerApi(new HttpClient(api) { BaseAddress = new Uri("https://api.example.invalid/") }));
        var app = builder.Build();
        app.Services.GetRequiredService<HostStorage>(); // Owned by this test host's service-provider disposal.
        app.UseRouting(); app.UseMiddleware<LearnerSecurity>(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapControllers(); configure?.Invoke(app); await app.StartAsync(); return (app, server);
    }
    private static string Cookies(InProcessServer.Response response)
        => string.Join("; ", response.Headers.SetCookie.Select(x => x!.Split(';')[0]));
    private static string Token(InProcessServer.Response response)
    {
        var match = Regex.Match(response.Body, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Rendered MVC form must include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    internal static string AuthCookie(WebApplication app, FakeApi api, string role = "Student")
    {
        var sessions = app.Services.GetRequiredService<LearnerSessions>();
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var id = sessions.Create(new LearnerSession { AccessToken = api.AccessToken, UserId = api.User,
            OrganizationId = api.Organization, OrganizationRole = role, ExpiresAt = expires });
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(LearnerSessions.Claim, id),
            new Claim(ClaimTypes.NameIdentifier, api.User.ToString())], CookieAuthenticationDefaults.AuthenticationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { ExpiresUtc = expires }, CookieAuthenticationDefaults.AuthenticationScheme);
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        return "__Host-LmsLearner=" + options.TicketDataFormat.Protect(ticket);
    }
    [Fact]
    public async Task Actual_MVC_GET_is_safe_and_POST_requires_bound_antiforgery_cookie()
    {
        using var api = new FakeApi(); var (app, server) = await Host(api); await using var application = app;
        var form = await server.Send("/account/login");
        Assert.Equal(200, form.Status); Assert.Equal(0, api.Requests);
        Assert.Contains("no-store", form.Headers.CacheControl.ToString()); Assert.Equal("no-referrer", form.Headers["Referrer-Policy"]);
        var rejected = await server.Send("/account/login", "POST", form: "Email=synthetic%40example.invalid&Password=Valid%21Pass123");
        Assert.Equal(400, rejected.Status); Assert.Equal(0, api.Requests);
        var signed = await server.Send("/account/login", "POST", Cookies(form),
            "__RequestVerificationToken=" + Uri.EscapeDataString(Token(form)) + "&Email=synthetic%40example.invalid&Password=Valid%21Pass123");
        Assert.Equal(302, signed.Status); Assert.Equal("/account/organizations", signed.Headers.Location.ToString());
        Assert.Contains("__Host-LmsLearner=", Cookies(signed));
        Assert.DoesNotContain("synthetic-server-only-token", Cookies(signed)); Assert.DoesNotContain("Valid!Pass123", signed.Body);
        Assert.Contains("secure", signed.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Revoked_API_session_rejects_real_cookie_and_removes_server_session()
    {
        using var api = new FakeApi(); var (app, server) = await Host(api); await using var application = app;
        var form = await server.Send("/account/login");
        var signed = await server.Send("/account/login", "POST", Cookies(form),
            "__RequestVerificationToken=" + Uri.EscapeDataString(Token(form)) + "&Email=synthetic%40example.invalid&Password=Valid%21Pass123");
        Assert.Equal(302, signed.Status);
        api.Revoked = true;
        var rejected = await server.Send("/account/organizations", cookie: Cookies(signed));
        Assert.Equal(302, rejected.Status); Assert.Contains("/account/login", rejected.Headers.Location.ToString());
        // Cookie validation's actual removal path executes; replay still cannot authenticate.
        var replay = await server.Send("/account/organizations", cookie: Cookies(signed));
        Assert.Equal(302, replay.Status); Assert.DoesNotContain("synthetic-server-only-token", rejected.Body);
    }
    [Fact]
    public async Task Anonymous_audit_and_download_requests_require_cookie_authentication()
    {
        using var api = new FakeApi(); var (app, server) = await Host(api); await using var application = app;
        var root = $"/learn/{api.Organization}/courses/{api.Course}/sections/{api.Section}/units/{api.Lesson}";
        foreach (var path in new[] { "/platform/audit", "/admin/" + Guid.NewGuid() + "/audit", root + $"/items/{api.Item}/media",
            root + $"/assignments/{Guid.NewGuid()}/submissions/{Guid.NewGuid()}/attachment" })
        {
            var response = await server.Send(path);
            if (path.EndsWith("/media", StringComparison.Ordinal)) Assert.Equal(401, response.Status);
            else { Assert.Equal(302, response.Status); Assert.Contains("/account/login", response.Headers.Location.ToString()); }
        }
        Assert.Equal(0, api.Requests);
    }
    [Fact]
    public async Task Authorized_private_PDF_download_and_foreign_nested_role_denials_use_real_pipeline()
    {
        using var api = new FakeApi(); var (app, server) = await Host(api); await using var application = app;
        var cookie = AuthCookie(app, api);
        var root = $"/learn/{api.Organization}/courses/{api.Course}/sections/{api.Section}/units/{api.Lesson}";
        var pdf = await server.Send(root + $"/items/{api.Item}/media?download=true", cookie: cookie);
        Assert.Equal(200, pdf.Status); Assert.Equal("application/pdf", pdf.Headers.ContentType.ToString());
        Assert.StartsWith("%PDF-", pdf.Body); Assert.Contains("attachment", pdf.Headers.ContentDisposition.ToString());
        Assert.Contains("no-store", pdf.Headers.CacheControl.ToString()); Assert.DoesNotContain("synthetic-server-only-token", pdf.Headers.ToString());
        var foreign = await server.Send(root.Replace(api.Organization.ToString(), Guid.NewGuid().ToString()) + $"/items/{api.Item}/media", cookie: cookie);
        Assert.Equal(403, foreign.Status);
        var nested = await server.Send(root.Replace(api.Lesson.ToString(), Guid.NewGuid().ToString()) + $"/items/{api.Item}/media", cookie: cookie);
        Assert.Equal(404, nested.Status);
        var instructor = await server.Send(root + $"/items/{api.Item}/media", cookie: AuthCookie(app, api, "Instructor"));
        Assert.Equal(403, instructor.Status);
    }
}
