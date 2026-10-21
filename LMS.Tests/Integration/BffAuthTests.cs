using System.Net;
using System.Security.Claims;
using System.Text.Json;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LMS.Tests.Integration;

[Trait("Category", "HostIntegration")]
public sealed class BffAuthTests
{
    internal sealed class Browser(InProcessServer server)
    {
        private readonly Dictionary<string, string> cookies = [];
        public string Cookie => string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value));
        public async Task<InProcessServer.Response> Send(string path, string method = "GET", object? data = null, string? csrf = null)
        {
            var response = await server.Send(path, method, Cookie, json: data is null ? null : JsonSerializer.Serialize(data), csrf: csrf);
            foreach (var header in response.Headers.SetCookie)
            {
                var pair = header!.Split(';')[0].Split('=', 2);
                if (pair[1].Length == 0) cookies.Remove(pair[0]); else cookies[pair[0]] = pair[1];
            }
            return response;
        }
        public async Task<string> Csrf()
        {
            var response = await Send("/bff/antiforgery"); Assert.Equal(200, response.Status);
            using var json = JsonDocument.Parse(response.Body);
            Assert.Equal("X-CSRF-TOKEN", json.RootElement.GetProperty("headerName").GetString());
            return json.RootElement.GetProperty("requestToken").GetString()!;
        }
        public void SetAuth(string cookie)
        {
            var pair = cookie.Split('=', 2); cookies[pair[0]] = pair[1];
        }
    }
    internal static JsonElement Json(InProcessServer.Response response)
    {
        Assert.StartsWith("application/json", response.Headers.ContentType.ToString());
        Assert.False(response.Headers.ContainsKey("Location"));
        using var doc = JsonDocument.Parse(response.Body); return doc.RootElement.Clone();
    }
    private static string FailProbe() => throw new InvalidOperationException("private-detail");
    private static object Credentials => new { email = "learner@example.test", password = "synthetic-password" };
    internal static string Id(WebApplication app, Browser browser)
    {
        var cookie = browser.Cookie.Split("; ").Single(x => x.StartsWith("__Host-LmsLearner="));
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        return options.TicketDataFormat.Unprotect(cookie.Split('=', 2)[1])!.Principal.FindFirstValue(LearnerSessions.Claim)!;
    }

    [Fact]
    public async Task Bff_transport_validation_preserves_HTTP_status_without_leaking_details()
    {
        using var fixture = new LearnerWebTests.Fixture();
        var http = fixture.Http(); http.Request.Path = "/bff/login";
        await new LearnerSecurity(_ => throw new BadHttpRequestException("private transport detail", 413)).InvokeAsync(http, fixture.Sessions);
        Assert.Equal(413, http.Response.StatusCode);
        using var body = JsonDocument.Parse(((MemoryStream)http.Response.Body).ToArray());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private transport detail", body.RootElement.ToString());
    }

    [Fact]
    public async Task Json_login_rotates_secure_cookie_without_exposing_server_secrets_and_MVC_still_renders()
    {
        using var api = new InProcessMvcTests.FakeApi(); var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server);
        var mvc = await server.Send("/account/login"); Assert.Equal(200, mvc.Status); Assert.Contains("__RequestVerificationToken", mvc.Body);
        var csrf = await browser.Csrf();
        var response = await browser.Send("/bff/login", "POST", Credentials, csrf);
        var body = Json(response); Assert.Equal(200, response.Status);
        Assert.Equal(api.User.ToString(), body.GetProperty("userId").GetString());
        Assert.True(body.GetProperty("requiresOrganizationSelection").GetBoolean());
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.Contains("secure", response.Headers.SetCookie.ToString());
        Assert.Contains("httponly", response.Headers.SetCookie.ToString());
        Assert.Contains("samesite=strict", response.Headers.SetCookie.ToString());
        var first = Id(app, browser);
        Assert.DoesNotContain(first, response.Body); Assert.DoesNotContain(api.AccessToken, response.Body);
        var stale = await browser.Send("/bff/logout", "POST", new { }, csrf);
        Assert.Equal(400, stale.Status); Assert.Equal("csrf_validation_failed", Json(stale).GetProperty("code").GetString());
        var second = await browser.Send("/bff/login", "POST", Credentials, await browser.Csrf()); Assert.Equal(200, second.Status);
        Assert.NotEqual(first, Id(app, browser));
        Assert.Null(app.Services.GetRequiredService<LearnerSessions>().Find(first));
        var mvcHome = await browser.Send("/account/home"); Assert.Equal(302, mvcHome.Status); Assert.Equal("/account/organizations", mvcHome.Headers.Location.ToString());
    }

    [Fact]
    public async Task Csrf_and_model_validation_failures_are_sanitized_JSON_before_upstream_login()
    {
        using var api = new InProcessMvcTests.FakeApi(); var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server);
        var missing = await browser.Send("/bff/login", "POST", Credentials);
        Assert.Equal(400, missing.Status); Assert.Equal("csrf_validation_failed", Json(missing).GetProperty("code").GetString());
        var invalid = await browser.Send("/bff/login", "POST", new { email = "not-an-email", password = "" }, await browser.Csrf());
        Assert.Equal(400, invalid.Status); Assert.Equal("validation_failed", Json(invalid).GetProperty("code").GetString());
        Assert.DoesNotContain("not-an-email", invalid.Body); Assert.Equal(0, api.Requests);
        var malformed = await server.Send("/bff/login", "POST", browser.Cookie, json: "{", csrf: await browser.Csrf());
        Assert.Equal(400, malformed.Status); Json(malformed);
    }

    [Fact]
    public async Task Session_reads_current_roles_and_revoked_selection_clears_only_selection()
    {
        using var api = new InProcessMvcTests.FakeApi { CurrentRole = "Instructor" };
        var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server); browser.SetAuth(InProcessMvcTests.AuthCookie(app, api, "Admin"));
        var sessions = app.Services.GetRequiredService<LearnerSessions>(); var old = Id(app, browser);
        var response = await browser.Send("/bff/session"); var body = Json(response);
        Assert.Equal(200, response.Status); Assert.Equal("Instructor", body.GetProperty("selectedOrganization").GetProperty("role").GetString());
        Assert.Null(sessions.Find(old)); var selectedId = Id(app, browser); var selected = sessions.Find(selectedId)!;
        api.OrganizationDenied = true;
        response = await browser.Send("/bff/session"); body = Json(response);
        Assert.Equal(200, response.Status); Assert.Equal(JsonValueKind.Null, body.GetProperty("selectedOrganization").ValueKind);
        Assert.Equal("organization_selection_cleared", body.GetProperty("recoveryCode").GetString());
        Assert.True(body.GetProperty("requiresOrganizationSelection").GetBoolean()); Assert.Null(sessions.Find(selectedId));
        var cleared = sessions.Find(Id(app, browser))!;
        Assert.Null(cleared.OrganizationId); Assert.Equal(selected.AccessToken, cleared.AccessToken);
        Assert.Equal(selected.UserId, cleared.UserId); Assert.Equal(selected.ExpiresAt, cleared.ExpiresAt);
        Assert.Equal(200, (await browser.Send("/bff/session")).Status);
    }

    [Fact]
    public async Task Unselected_session_checks_stored_platform_authority()
    {
        using var api = new InProcessMvcTests.FakeApi { Platform = true };
        var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server);
        await browser.Send("/bff/login", "POST", Credentials, await browser.Csrf());
        var session = Json(await browser.Send("/bff/session"));
        Assert.True(session.GetProperty("isSuperAdmin").GetBoolean()); Assert.Equal("/platform", session.GetProperty("homePath").GetString());
        api.Platform = false;
        Assert.False(Json(await browser.Send("/bff/session")).GetProperty("isSuperAdmin").GetBoolean());
    }

    [Fact]
    public async Task Logout_requires_CSRF_revokes_session_expires_cookie_and_is_idempotent()
    {
        using var api = new InProcessMvcTests.FakeApi(); var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server);
        await browser.Send("/bff/login", "POST", Credentials, await browser.Csrf()); var id = Id(app, browser);
        Assert.Equal(400, (await browser.Send("/bff/logout", "POST", new { })).Status);
        var response = await browser.Send("/bff/logout", "POST", new { }, await browser.Csrf());
        Assert.Equal(204, response.Status); Assert.Contains("expires=", response.Headers.SetCookie.ToString());
        Assert.Null(app.Services.GetRequiredService<LearnerSessions>().Find(id));
        Assert.Equal(204, (await browser.Send("/bff/logout", "POST", new { }, await browser.Csrf())).Status);
        var expired = await browser.Send("/bff/session"); Assert.Equal(401, expired.Status); Json(expired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_and_upstream_validation_outage_keep_existing_signout_behavior(bool outage)
    {
        using var api = new InProcessMvcTests.FakeApi(); var (app, server) = await InProcessMvcTests.Host(api); await using var host = app;
        var browser = new Browser(server); browser.SetAuth(InProcessMvcTests.AuthCookie(app, api)); var id = Id(app, browser);
        api.SessionOutage = outage; api.Revoked = !outage;
        var response = await browser.Send("/bff/session");
        Assert.Equal(401, response.Status); Assert.Equal("session_expired", Json(response).GetProperty("code").GetString());
        Assert.Null(app.Services.GetRequiredService<LearnerSessions>().Find(id));
    }

    [Fact]
    public async Task Challenges_forbidden_exceptions_and_rate_limits_return_JSON_without_redirects()
    {
        using var api = new InProcessMvcTests.FakeApi { LoginStatus = HttpStatusCode.Unauthorized };
        var (app, server) = await InProcessMvcTests.Host(api, web => {
            web.MapGet("/bff/forbidden-probe", () => "never").RequireAuthorization(p => p.RequireClaim("never"));
            web.MapGet("/bff/error-probe", () => FailProbe()).AllowAnonymous();
        }); await using var host = app;
        var browser = new Browser(server);
        var challenge = await browser.Send("/bff/session"); Assert.Equal(401, challenge.Status); Json(challenge);
        browser.SetAuth(InProcessMvcTests.AuthCookie(app, api));
        var forbidden = await browser.Send("/bff/forbidden-probe"); Assert.Equal(403, forbidden.Status); Json(forbidden);
        var failure = await browser.Send("/bff/error-probe"); Assert.Equal(500, failure.Status); Json(failure); Assert.DoesNotContain("private-detail", failure.Body);
        browser = new Browser(server); var csrf = await browser.Csrf();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var denied = await browser.Send("/bff/login", "POST", Credentials, csrf);
            Assert.Equal(401, denied.Status); Assert.Equal("invalid_credentials", Json(denied).GetProperty("code").GetString());
        }
        var limited = await browser.Send("/bff/login", "POST", Credentials, csrf);
        Assert.Equal(429, limited.Status); Assert.Equal("rate_limited", Json(limited).GetProperty("code").GetString());
    }
    [Fact]
    public void Shared_conflict_error_is_not_specific_to_enrollment_and_stays_sanitized()
    {
        var error=BffJson.Error(409);
        Assert.Equal("conflict",error.Code);
        Assert.Contains("stored state",error.Message);
        Assert.DoesNotContain("enrollment",error.Message,StringComparison.OrdinalIgnoreCase);
    }
}
