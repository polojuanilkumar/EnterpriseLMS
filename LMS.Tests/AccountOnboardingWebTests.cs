using System.Security.Claims;
using LMS.Infrastructure.Identity;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
namespace LMS.Tests;
public sealed class AccountOnboardingWebTests
{
    [Theory][InlineData(null)][InlineData("http://trusted.example")][InlineData("https://user@trusted.example/")]
    [InlineData("https://trusted.example/path")][InlineData("https://trusted.example/?token=oops")][InlineData("https://trusted.example/#fragment")]
    public void Public_link_origin_requires_explicit_clean_HTTPS(string? configured)
        => Assert.Throws<InvalidOperationException>(()=>AccountLinkPolicy.TrustedOrigin(configured));
    [Fact] public void Public_link_origin_is_configuration_only()
        => Assert.Equal("trusted.example",AccountLinkPolicy.TrustedOrigin("https://trusted.example/").Host);
    [Theory][InlineData("Invitation")][InlineData("Reset")][InlineData("Forgot")]
    public async Task Actual_Razor_forms_encode_fields_hide_passwords_and_have_no_external_resources(string view)
    {
        using var services=LearnerWebTests.RenderServices();
        var model=new RecoveryForm {Email="<script>email</script>",Organization="<script>organization</script>",Role="<script>role</script>",Password="never-redisplay-password"};
        var html=await Render(services,"/Views/AccountOnboarding/"+view+".cshtml",model);
        Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;email&lt;/script&gt;",html);
        Assert.DoesNotContain("never-redisplay-password",html);Assert.Contains("__RequestVerificationToken",html);
        Assert.DoesNotContain("<script ",html);Assert.DoesNotContain("<iframe",html);Assert.DoesNotContain("https://",html);
        if(view=="Invitation")Assert.Contains("&lt;script&gt;organization&lt;/script&gt;",html);
    }
    [Fact]
    public async Task Initial_invitation_GET_redirects_without_any_API_call_or_acceptance()
    {
        using var services=LearnerWebTests.RenderServices();using var http=new HttpClient(new NoRequests()) {BaseAddress=new Uri("https://api.example.test/")};
        var context=new DefaultHttpContext {RequestServices=services};
        var controller=new AccountOnboardingController(new LearnerApi(http),services.GetRequiredService<LearnerSessions>(),services.GetRequiredService<AccountLinks>())
            {ControllerContext=new() {HttpContext=context}};
        var result=Assert.IsType<RedirectResult>(await controller.Invitation("secret-token",default));
        Assert.Equal("/account/invitation",result.Url);Assert.DoesNotContain("secret-token",context.Response.Headers.SetCookie.ToString());
        Assert.Equal("no-store",context.Response.Headers.CacheControl);Assert.Equal("no-referrer",context.Response.Headers["Referrer-Policy"]);
        Assert.Contains("default-src 'none'",context.Response.Headers["Content-Security-Policy"].ToString());
        var reset=Assert.IsType<RedirectResult>(controller.Reset("secret-reset","invited@example.test"));
        Assert.Equal("/account/reset-password",reset.Url);
    }
    [Fact]
    public async Task Web_cookie_is_rejected_and_server_session_removed_on_revoked_JWT_response()
    {
        using var services=LearnerWebTests.RenderServices(false);using var scope=services.CreateScope();
        var sessions=services.GetRequiredService<LearnerSessions>();
        var id=sessions.Create(new() {UserId=Guid.NewGuid(),AccessToken="revoked-jwt",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)});
        var http=new DefaultHttpContext {RequestServices=scope.ServiceProvider};
        var options=services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket=new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(LearnerSessions.Claim,id)],"cookie")),CookieAuthenticationDefaults.AuthenticationScheme);
        var validation=new CookieValidatePrincipalContext(http,new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme,null,typeof(CookieAuthenticationHandler)),options,ticket);
        await options.Events.OnValidatePrincipal(validation);
        Assert.Null(validation.Principal);Assert.Null(sessions.Find(id));
    }
    private sealed class NoRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            => throw new InvalidOperationException("GET entry must not call the API.");
    }
    private static async Task<string> Render<T>(ServiceProvider services,string path,T model)
    {
        using var scope=services.CreateScope();var http=new DefaultHttpContext {RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());
        var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var found=engine.GetView(null,path,false);
        Assert.True(found.Success,string.Join(",",found.SearchedLocations??[]));using var writer=new StringWriter();
        var data=new ViewDataDictionary<T>(new EmptyModelMetadataProvider(),new ModelStateDictionary()) {Model=model};
        var temp=new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        await found.View.RenderAsync(new ViewContext(action,found.View,data,temp,writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
