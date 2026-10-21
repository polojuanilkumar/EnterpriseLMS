using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Infrastructure.Identity;
using LMS.Web.Controllers;
using LMS.Web.Models;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;
public sealed class BffPlatformAccountTests
{
    private static HttpResponseMessage Data(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data=value})};
    [Theory][InlineData(null,"account/invitation")][InlineData("Mvc","account/invitation")][InlineData("Angular","account/angular/invitation")]
    public void Trusted_link_destination_is_explicit_and_MVC_is_preserved(string? destination,string expected)
        =>Assert.Equal(expected,AccountLinkPolicy.Destination("account/invitation",destination));
    [Theory][InlineData("https://attacker.test/")][InlineData("angular")]
    public void Untrusted_link_destinations_are_rejected(string destination)
        =>Assert.Throws<InvalidOperationException>(()=>AccountLinkPolicy.Destination("account/invitation",destination));
    [Theory][InlineData(false,403)][InlineData(true,200)]
    public async Task Platform_reads_revalidate_stored_role_without_selected_organization(bool platform,int expected)
    {
        using var api=new InProcessMvcTests.FakeApi{Platform=platform};
        api.StudentResponse=r=>{Assert.False(r.Headers.Contains("X-Organization-Id"));return r.RequestUri!.AbsolutePath=="/api/organizations"?Data(new {organizationId=(Guid?)null,page=1,pageSize=20,total=1,items=new[]{new OrganizationView(api.Organization,"<script>Organization</script>",true,"ORG")}}):null;};
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var browser=new Browser(server);
        Assert.Equal(200,(await browser.Send("/bff/login","POST",new {email="synthetic@example.test",password="synthetic"},await browser.Csrf())).Status);
        var response=await browser.Send("/bff/platform/organizations");Assert.Equal(expected,response.Status);Assert.DoesNotContain(api.AccessToken,response.Body);
        if(platform){Assert.Null(app.Services.GetRequiredService<LMS.Web.Services.LearnerSessions>().Find(Id(app,browser))!.OrganizationId);api.Platform=false;Assert.Equal(403,(await browser.Send("/bff/platform/organizations")).Status);}
    }
    [Fact]
    public async Task Creation_requires_CSRF_confirmation_and_sends_exact_narrow_payload_without_tenant_authority()
    {
        using var api=new InProcessMvcTests.FakeApi{Platform=true};var writes=new List<string>();
        api.StudentResponse=r=>{Assert.False(r.Headers.Contains("X-Organization-Id"));Assert.Equal("/api/organizations",r.RequestUri!.AbsolutePath);writes.Add(r.Content!.ReadAsStringAsync().Result);return new(HttpStatusCode.NoContent);};
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);b.SetAuth(InProcessMvcTests.AuthCookie(app,api));
        var body=new {code="NEW",name="New organization",initialAdminEmail="exact@example.test",confirmed=true};
        Assert.Equal(400,(await b.Send("/bff/platform/organizations","POST",body)).Status);Assert.Empty(writes);
        var csrf=await b.Csrf();Assert.Equal(400,(await b.Send("/bff/platform/organizations","POST",new {code="NEW",name="New",initialAdminEmail="exact@example.test",confirmed=false},csrf)).Status);Assert.Empty(writes);
        Assert.Equal(204,(await b.Send("/bff/platform/organizations","POST",body,csrf)).Status);Assert.Contains("exact@example.test",Assert.Single(writes));
    }
    [Theory][InlineData(false,200)][InlineData(true,502)]
    public async Task Platform_audit_cannot_include_organization_events_or_raw_metadata(bool foreign,int expected)
    {
        using var api=new InProcessMvcTests.FakeApi{Platform=true};var id=Guid.NewGuid();
        api.StudentResponse=r=>r.RequestUri!.AbsolutePath=="/api/audit/platform"?Data(new AuditViewPage(null,1,20,1,new(){FromUtc=DateTimeOffset.UtcNow.AddDays(-7),ToUtc=DateTimeOffset.UtcNow},[new(id,DateTimeOffset.UtcNow,api.User,foreign?api.Organization:null,foreign?"organization":"account",foreign?"organization.created":"account.password-reset",foreign?"organization":"account",api.User,"succeeded",Guid.NewGuid().ToString("N"),"{}")])):null;
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);b.SetAuth(InProcessMvcTests.AuthCookie(app,api));var response=await b.Send("/bff/platform/audit");Assert.Equal(expected,response.Status);
        if(!foreign){Assert.Equal("account",Json(response).GetProperty("items")[0].GetProperty("scope").GetString());Assert.DoesNotContain("token",response.Body,StringComparison.OrdinalIgnoreCase);}
    }
    [Theory][InlineData("{\"token\":\"secret\"}")][InlineData("[]")][InlineData("{\"email\":\"private@example.test\"}")]
    public void Platform_metadata_is_empty_allowlist_only(string metadata)
        =>Assert.Throws<LMS.Web.Services.WebApiException>(()=>BffPlatformController.PlatformAudit(new(Guid.NewGuid(),DateTimeOffset.UtcNow,null,null,"account","account.password-reset","account",Guid.NewGuid(),"succeeded",Guid.NewGuid().ToString("N"),metadata)));
    [Theory][InlineData("invitation","invite")][InlineData("reset-password","reset")]
    public async Task Token_entry_redirects_clean_without_calling_API_or_mutating(string page,string purpose)
    {
        using var api=new InProcessMvcTests.FakeApi();var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);
        var response=await b.Send("/account/angular/"+page+"?token=PRIVATE-token&email=invited%40example.test");Assert.Equal(302,response.Status);Assert.Equal("/app/account/"+page,response.Headers.Location);
        Assert.DoesNotContain("PRIVATE-token",response.Headers.SetCookie.ToString());Assert.DoesNotContain("PRIVATE-token",response.Body);Assert.Contains("__Host-LmsLink-"+purpose,b.Cookie);Assert.Equal(0,api.Requests);
        Assert.Equal("no-store",response.Headers.CacheControl);Assert.Equal("no-referrer",response.Headers["Referrer-Policy"]);Assert.Contains("default-src 'none'",response.Headers["Content-Security-Policy"].ToString());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Forgot_is_generic_even_when_delivery_is_uncertain(bool outage)
    {
        using var api=new InProcessMvcTests.FakeApi();int sent=0;
        api.StudentResponse=r=>{if(r.RequestUri!.AbsolutePath=="/api/account/antiforgery")return new(HttpStatusCode.OK){Content=JsonContent.Create(new {token="server-csrf"})};sent++;if(outage)throw new HttpRequestException("private diagnostic");return new(HttpStatusCode.OK){Content=JsonContent.Create(new {message="generic"})};};
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);
        Assert.Equal(400,(await b.Send("/bff/account/forgot-password","POST",new {email="unknown@example.test"})).Status);Assert.Equal(0,sent);
        var response=await b.Send("/bff/account/forgot-password","POST",new {email="unknown@example.test"},await b.Csrf());Assert.Equal(200,response.Status);Assert.Equal("If the account is eligible, password recovery instructions will be sent.",Json(response).GetProperty("message").GetString());Assert.Equal(1,sent);
    }
    [Theory][InlineData("reset-password")][InlineData("invitation")]
    public async Task Final_actions_require_CSRF_and_confirmation_consume_private_link_once_and_never_replay_ambiguous_API_calls(string page)
    {
        using var api=new InProcessMvcTests.FakeApi();int mutations=0;
        api.StudentResponse=r=>{
            if(r.RequestUri!.AbsolutePath=="/api/account/antiforgery")return new(HttpStatusCode.OK){Content=JsonContent.Create(new {token="server-csrf"})};
            Assert.Equal("server-csrf",Assert.Single(r.Headers.GetValues("X-CSRF-TOKEN")));var body=r.Content!.ReadAsStringAsync().Result;Assert.Contains("PRIVATE-token",body);Assert.DoesNotContain("organizationId",body,StringComparison.OrdinalIgnoreCase);mutations++;throw new HttpRequestException("private diagnostic");};
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);await b.Send("/account/angular/"+page+"?token=PRIVATE-token&email=invited%40example.test");
        var body=new {password="PRIVATE-password",firstName="First",lastName="Last",confirmed=true};
        Assert.Equal(400,(await b.Send("/bff/account/"+page,"POST",body)).Status);Assert.Equal(0,mutations);var csrf=await b.Csrf();
        if(page=="invitation"){Assert.Equal(400,(await b.Send("/bff/account/invitation","POST",new {confirmed=false},csrf)).Status);Assert.Equal(0,mutations);}
        var response=await b.Send("/bff/account/"+page,"POST",body,csrf);Assert.Equal(502,response.Status);Assert.DoesNotContain("PRIVATE",response.Body);Assert.Equal(1,mutations);
        Assert.Equal(400,(await b.Send("/bff/account/"+page,"POST",body,csrf)).Status);Assert.Equal(1,mutations);
    }
    [Fact]
    public async Task Account_BFF_rate_limit_stays_JSON_and_no_get_acceptance_exists()
    {
        using var api=new InProcessMvcTests.FakeApi();var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);
        for(var n=0;n<10;n++)Assert.Equal(400,(await b.Send("/bff/account/reset-password")).Status);
        var response=await b.Send("/bff/account/reset-password");Assert.Equal(429,response.Status);Assert.Equal("rate_limited",Json(response).GetProperty("code").GetString());Assert.Equal(0,api.Requests);
    }
    [Fact]
    public async Task Matching_account_identity_is_forwarded_server_side_only_and_preview_never_accepts()
    {
        using var api=new InProcessMvcTests.FakeApi();int previews=0,accepts=0;
        api.StudentResponse=r=>{
            Assert.False(r.Headers.Contains("X-Organization-Id"));
            if(r.RequestUri!.AbsolutePath=="/api/account/antiforgery")return new(HttpStatusCode.OK){Content=JsonContent.Create(new {token="server-csrf"})};
            if(r.RequestUri.AbsolutePath=="/api/account/invitations/preview") {Assert.Null(r.Headers.Authorization);previews++;return new(HttpStatusCode.OK){Content=JsonContent.Create(new {email="invited@example.test",organization="Test",role=1,existingAccount=true})};}
            Assert.Equal("/api/account/invitations/accept",r.RequestUri.AbsolutePath);Assert.Equal(api.AccessToken,r.Headers.Authorization!.Parameter);accepts++;return new(HttpStatusCode.OK){Content=JsonContent.Create(new {message="accepted"})};
        };
        var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;var b=new Browser(server);await b.Send("/account/angular/invitation?token=PRIVATE-token");
        var preview=await b.Send("/bff/account/invitation");Assert.Equal(200,preview.Status);Assert.Equal(1,previews);Assert.Equal(0,accepts);Assert.DoesNotContain("PRIVATE-token",preview.Body);
        b.SetAuth(InProcessMvcTests.AuthCookie(app,api));Assert.Equal(204,(await b.Send("/bff/account/invitation","POST",new {confirmed=true},await b.Csrf())).Status);Assert.Equal(1,accepts);
        Assert.Equal(400,(await b.Send("/bff/account/invitation")).Status);Assert.Equal(1,previews);
    }
    [Theory][InlineData("/account/angular/invitation")][InlineData("/account/angular/reset-password")]
    public async Task Missing_token_is_safely_rejected(string path)
    {
        using var api=new InProcessMvcTests.FakeApi();var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;
        Assert.Equal(400,(await server.Send(path)).Status);Assert.Equal(0,api.Requests);
    }
    [Fact]
    public async Task Opt_in_SPA_fallback_is_scoped_to_app_routes_and_account_headers_remain_restrictive()
    {
        using var api=new InProcessMvcTests.FakeApi();var (app,server)=await InProcessMvcTests.Host(api);await using var host=app;
        Assert.Equal(404,(await server.Send("/app/account/invitation")).Status);
        app.Configuration["Angular:Enabled"]="true";
        var env=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var root=Path.Combine(env.ContentRootPath,"wwwroot","app");Directory.CreateDirectory(root);await File.WriteAllTextAsync(Path.Combine(root,"index.html"),"<!doctype html><title>isolated SPA</title>");
        var response=await server.Send("/app/account/invitation");Assert.Equal(200,response.Status);Assert.Contains("isolated SPA",response.Body);
        Assert.Equal("no-store",response.Headers.CacheControl);Assert.Contains("frame-src 'none'",response.Headers["Content-Security-Policy"].ToString());Assert.Equal("no-referrer",response.Headers["Referrer-Policy"]);
        Assert.Equal(404,(await server.Send("/app/missing.js")).Status);Assert.Equal(401,(await server.Send("/bff/platform/organizations")).Status);
    }
}
