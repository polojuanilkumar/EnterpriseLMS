using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Web.Services;

public static class LearnerWebRegistration
{
    public static IServiceCollection AddLearnerWeb(this IServiceCollection services,IConfiguration configuration)
    {
services.AddControllersWithViews(options=>{options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());options.Filters.Add(new BffJsonFilter());});
var protection = services.AddDataProtection();
if(configuration["DataProtection:KeyPath"] is string keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "LMS.Web");
services.AddMemoryCache();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<LearnerSessions>();
services.AddSingleton<AccountLinks>();
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options=>
{
    options.Cookie.Name="__Host-LmsLearner";
    options.Cookie.HttpOnly=true;
    options.Cookie.SecurePolicy=CookieSecurePolicy.Always;
    options.Cookie.SameSite=SameSiteMode.Strict;
    options.Cookie.Path="/";
    options.LoginPath="/account/login";
    options.SlidingExpiration=false;
    options.Events.OnValidatePrincipal=async context=>
    {
        var sessions=context.HttpContext.RequestServices.GetRequiredService<LearnerSessions>();
        var id=context.Principal?.FindFirstValue(LearnerSessions.Claim);
        var session=sessions.Find(id);
        bool valid=false;
        if(session is not null)
        {
            try { valid=await context.HttpContext.RequestServices.GetRequiredService<LearnerApi>().SessionValid(session,context.HttpContext.RequestAborted); }
            catch(HttpRequestException) { }
            catch(TaskCanceledException) { }
        }
        if(!valid){sessions.Remove(id);context.RejectPrincipal();}
    };    options.Events.OnRedirectToLogin=context=>
    {
        if(BffJson.Applies(context.HttpContext))return BffJson.Write(context.HttpContext,401);
        if(context.Request.Path.Value?.EndsWith("/media",StringComparison.Ordinal)==true)context.Response.StatusCode=401;
        else context.Response.Redirect("/account/login");
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied=context=>{if(BffJson.Applies(context.HttpContext))return BffJson.Write(context.HttpContext,403);context.Response.StatusCode=403;return Task.CompletedTask;};
});
services.AddAuthorization(options=>options.FallbackPolicy=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
services.AddAntiforgery(options=>
{
    options.HeaderName="X-CSRF-TOKEN";
    options.Cookie.Name="__Host-LmsAntiforgery";
    options.Cookie.SecurePolicy=CookieSecurePolicy.Always;
    options.Cookie.HttpOnly=true;
    options.Cookie.SameSite=SameSiteMode.Strict;
});
services.AddRateLimiter(options=>
{
    options.RejectionStatusCode=429;
    options.OnRejected=async (context,token)=>
    {
        if(BffJson.Applies(context.HttpContext))await BffJson.Write(context.HttpContext,429);
    };
    options.AddPolicy("organization-administration",context=>RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _=>new FixedWindowRateLimiterOptions {PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
var apiAddress=configuration["LmsApi:BaseUrl"];
if(!Uri.TryCreate(apiAddress,UriKind.Absolute,out var apiUri) || apiUri.Scheme!="https" || apiUri.UserInfo.Length!=0
    || apiUri.AbsolutePath!="/" || apiUri.Query.Length!=0 || apiUri.Fragment.Length!=0)
    throw new InvalidOperationException("Configure LmsApi:BaseUrl as the trusted HTTPS API origin.");
services.AddHttpClient<LearnerApi>(client=>{client.BaseAddress=apiUri;client.Timeout=TimeSpan.FromSeconds(60);})
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false})
    .RemoveAllLoggers(); // No outbound Authorization headers or login bodies in HttpClient logs.

        return services;
    }
}

