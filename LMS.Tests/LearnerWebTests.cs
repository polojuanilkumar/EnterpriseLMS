using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class LearnerWebTests
{
    [Fact]
    public void Server_sessions_are_expiring_revocable_and_organization_bound()
    {
        using var f=new Fixture();
        var stored=f.Sessions.Find(f.SessionId)!;
        Assert.Equal("server-only-test-token",stored.AccessToken);
        Assert.DoesNotContain(stored.AccessToken,f.SessionId);
        Assert.Throws<WebApiException>(()=>f.Sessions.Require(f.User,Guid.NewGuid()));
        Assert.Throws<WebApiException>(()=>f.Sessions.Require(new ClaimsPrincipal(),f.Org));
        f.Clock.Now=stored.ExpiresAt.AddSeconds(1);
        Assert.Null(f.Sessions.Find(f.SessionId));
        Assert.Throws<WebApiException>(()=>f.Sessions.Create(stored));
    }
    [Fact]
    public async Task Login_rotates_session_and_cookie_principal_never_contains_api_token()
    {
        using var f=new Fixture();
        ClaimsPrincipal? signedIn=null;
        f.Auth.Setup(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()))
            .Callback((HttpContext _,string? _,ClaimsPrincipal p,AuthenticationProperties? _) => signedIn=p).Returns(Task.CompletedTask);
        var controller=f.Account();
        var response=await controller.Login(new LoginForm {Email="learner@example.test",Password="test-password"},default);
        Assert.IsType<RedirectToActionResult>(response);
        Assert.Null(f.Sessions.Find(f.SessionId));Assert.NotNull(signedIn);
        Assert.DoesNotContain(signedIn.Claims,x=>x.Value.Contains("server-only-test-token"));
        var session=f.Sessions.Require(signedIn);
        Assert.Null(session.OrganizationId);
        Assert.Equal("server-only-test-token",session.AccessToken);
        Assert.Single(f.Handler.Requests);
        Assert.Equal("/api/Auth/login",f.Handler.Requests[0].Path);
        Assert.Null(f.Handler.Requests[0].Authorization);
    }
    [Fact]
    public async Task Failed_login_does_not_issue_session_or_redisplay_password()
    {
        using var f=new Fixture();f.Handler.FailurePath="/api/Auth/login";f.Handler.Failure=401;
        var controller=f.Account();controller.ModelState.SetModelValue("Password","secret-from-form","secret-from-form");controller.ModelState.MarkFieldValid("Password");
        var view=Assert.IsType<ViewResult>(await controller.Login(new LoginForm {Email="a@example.test",Password="secret-from-form"},default));
        Assert.Equal("",Assert.IsType<LoginForm>(view.Model).Password);
        Assert.False(controller.ModelState.ContainsKey("Password"));Assert.Equal(401,controller.Response.StatusCode);
        f.Auth.Verify(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()),Times.Never);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Organization_selection_requires_membership_and_verified_context(bool foreign)
    {
        using var f=new Fixture();var controller=f.Account();
        if(!foreign){f.Handler.FailurePath=$"/api/organizations/{f.Org}/context";f.Handler.Failure=403;}
        var error=await Assert.ThrowsAsync<WebApiException>(()=>controller.SelectOrganization(foreign?Guid.NewGuid():f.Org,default));
        Assert.Equal(403,error.Status);Assert.NotNull(f.Sessions.Find(f.SessionId));
        f.Auth.Verify(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()),Times.Never);
        if(foreign)Assert.Single(f.Handler.Requests);
    }
    [Fact]
    public async Task Valid_selection_rotates_session_and_logout_revokes_it()
    {
        using var f=new Fixture();ClaimsPrincipal? signedIn=null;
        f.Auth.Setup(x=>x.SignInAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<ClaimsPrincipal>(),It.IsAny<AuthenticationProperties>()))
            .Callback((HttpContext _,string? _,ClaimsPrincipal p,AuthenticationProperties? _) => signedIn=p).Returns(Task.CompletedTask);
        var controller=f.Account();
        var result=Assert.IsType<RedirectResult>(await controller.SelectOrganization(f.Org,default));
        Assert.Equal($"/learn/{f.Org}/courses",result.Url);Assert.Null(f.Sessions.Find(f.SessionId));
        Assert.Equal(f.Org,f.Sessions.Require(signedIn!).OrganizationId);
        controller.HttpContext.User=signedIn!;await controller.Logout();
        Assert.Null(f.Sessions.Find(signedIn!.FindFirstValue(LearnerSessions.Claim)));
    }
    [Fact]
    public async Task Courses_show_only_published_active_or_completed_enrollments()
    {
        using var f=new Fixture();
        f.Handler.Courses=[new(f.Course,"Allowed","",true),new(Guid.NewGuid(),"Not enrolled","",true),new(Guid.NewGuid(),"Draft","",false)];
        Assert.Equal(f.Course,Assert.Single(await f.Api.Courses(f.Session,f.Org,default)).Id);
    }
    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)]
    public async Task Learner_gate_denials_prevent_content_and_media_requests(int status)
    {
        using var f=new Fixture();f.Handler.FailurePath=$"/api/Courses/{f.Course}/progress";f.Handler.Failure=status;
        var error=await Assert.ThrowsAsync<WebApiException>(()=>f.Learn().Media(f.Org,f.Course,f.Section,f.Lesson,f.Item,false,default));
        Assert.Equal(status,error.Status);Assert.Single(f.Handler.Requests);
    }
    [Theory]
    [InlineData("organization")] [InlineData("course")] [InlineData("section")] [InlineData("lesson")] [InlineData("item")]
    public async Task Mismatched_nested_routes_cannot_fetch_private_media(string mismatch)
    {
        using var f=new Fixture();
        var error=await Assert.ThrowsAsync<WebApiException>(()=>f.Learn().Media(mismatch=="organization"?Guid.NewGuid():f.Org,
            mismatch=="course"?Guid.NewGuid():f.Course,mismatch=="section"?Guid.NewGuid():f.Section,
            mismatch=="lesson"?Guid.NewGuid():f.Lesson,mismatch=="item"?Guid.NewGuid():f.Item,false,default));
        Assert.True(error.Status is 403 or 404);
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.EndsWith("/media"));
    }
    [Theory]
    [InlineData("GET",206,"bytes=2-4",3)] [InlineData("HEAD",200,"",0)] [InlineData("GET",416,"bytes=999-",0)]
    public async Task Proxy_forwards_range_head_and_session_headers_without_leaking_credentials(string method,int status,string range,int length)
    {
        using var f=new Fixture();f.Handler.MediaStatus=status;
        var controller=f.Learn();controller.Request.Method=method;controller.Request.Headers.Range=range;controller.Request.Headers.IfRange="\"version\"";
        controller.Request.Headers["X-Organization-Id"]=Guid.NewGuid().ToString();
        await controller.Media(f.Org,f.Course,f.Section,f.Lesson,f.Item,false,default);
        var request=f.Handler.Requests.Last();
        Assert.Equal(method,request.Method);Assert.Equal(string.IsNullOrEmpty(range)?null:range,request.Range);
        Assert.Equal("\"version\"",request.IfRange);Assert.Equal(f.Org.ToString(),request.Organization);
        Assert.Equal("Bearer server-only-test-token",request.Authorization);
        Assert.Equal(status,controller.Response.StatusCode);Assert.Equal(length,controller.Response.Body.Length);
        Assert.False(controller.Response.Headers.ContainsKey("Set-Cookie"));Assert.False(controller.Response.Headers.ContainsKey("Authorization"));
        Assert.Equal("private, no-store",controller.Response.Headers.CacheControl);
        if(status==206)Assert.Equal("bytes 2-4/10",controller.Response.Headers.ContentRange);
    }
    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)]
    public async Task Media_denials_do_not_forward_upstream_private_body(int status)
    {
        using var f=new Fixture();f.Handler.MediaStatus=status;
        var controller=f.Learn();
        var error=await Assert.ThrowsAsync<WebApiException>(()=>controller.Media(f.Org,f.Course,f.Section,f.Lesson,f.Item,false,default));
        Assert.Equal(status,error.Status);Assert.Equal(0,controller.Response.Body.Length);
    }
    [Fact]
    public async Task Unexpected_upstream_mime_cannot_be_served_as_same_origin_html()
    {
        using var f=new Fixture();f.Handler.WrongMime=true;var controller=f.Learn();
        var error=await Assert.ThrowsAsync<WebApiException>(()=>controller.Media(f.Org,f.Course,f.Section,f.Lesson,f.Item,false,default));
        Assert.Equal(502,error.Status);Assert.Equal(0,controller.Response.Body.Length);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pdf_proxy_is_sandboxed_and_has_generated_disposition(bool download)
    {
        using var f=new Fixture();f.Handler.ItemType=5;var controller=f.Learn();
        await controller.Media(f.Org,f.Course,f.Section,f.Lesson,f.Item,download,default);
        Assert.Equal("application/pdf",controller.Response.ContentType);
        Assert.Contains("sandbox;",controller.Response.Headers["Content-Security-Policy"].ToString());
        Assert.StartsWith(download?"attachment;":"inline;",controller.Response.Headers.ContentDisposition.ToString());
        Assert.DoesNotContain("allow-same-origin",controller.Response.Headers["Content-Security-Policy"].ToString());
    }
    [Fact]
    public async Task Items_suppress_legacy_and_empty_items_preserve_legacy()
    {
        using var f=new Fixture();f.Handler.Legacy=new("<b>legacy</b>","https://example.test/video");
        var unit=await f.Api.Unit(f.Session,f.Org,f.Course,f.Section,f.Lesson,default);Assert.Null(unit.Content.Legacy);
        f.Handler.EmptyItems=true;
        unit=await f.Api.Unit(f.Session,f.Org,f.Course,f.Section,f.Lesson,default);
        Assert.Equal("<b>legacy</b>",unit.Content.Legacy!.Content);
    }
    [Theory]
    [InlineData("dQw4w9WgXcQ",true)] [InlineData("abc\" onload=alert(1)",false)]
    [InlineData("https://evil.test/embed",false)] [InlineData("",false)]
    public void Only_normalized_youtube_ids_are_embedded(string id,bool valid)
        => Assert.Equal(valid,SafeContent.YouTubeEmbed(id) is not null);
    [Theory]
    [InlineData("javascript:alert(1)",false)] [InlineData("https://evil.test/v",false)]
    [InlineData("https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ",false)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ",true)]
    public void Legacy_urls_are_not_loaded_unless_valid_youtube(string url,bool valid)
        => Assert.Equal(valid,SafeContent.LegacyYouTube(url) is not null);
    [Fact]
    public async Task Razor_encodes_text_titles_and_legacy_and_uses_cookie_proxy_urls()
    {
        using var f=new Fixture();
        using var provider=RenderServices();
        var unit=new UnitPage(f.Org,f.Course,f.Section,new(f.Lesson,f.Section,"<script>title</script>",null,true,0),
            new([new(f.Item,f.Lesson,1,"<img onerror=evil>",0,false,"<script>body</script>",null),
                new(Guid.NewGuid(),f.Lesson,5,"PDF",1,false,null,null)],null));
        var html=await Render(provider,unit);
        Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;body&lt;/script&gt;",html);
        Assert.Contains("sandbox",html);Assert.Contains("/items/",html);Assert.DoesNotContain("server-only-test-token",html);
        var legacy=unit with {Content=new([],new("<script>legacy</script>","javascript:alert(1)"))};
        html=await Render(provider,legacy);
        Assert.Contains("&lt;script&gt;legacy&lt;/script&gt;",html);Assert.DoesNotContain("src=\"javascript:",html);
    }
    [Fact]
    public void Actual_registration_enforces_secure_cookies_csrf_and_authentication()
    {
        using var services=RenderServices();
        var cookies=services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.True(cookies.Cookie.HttpOnly);Assert.Equal(CookieSecurePolicy.Always,cookies.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict,cookies.Cookie.SameSite);Assert.False(cookies.SlidingExpiration);
        Assert.StartsWith("__Host-",cookies.Cookie.Name);
        Assert.Contains(services.GetRequiredService<IOptions<MvcOptions>>().Value.Filters,x=>x is AutoValidateAntiforgeryTokenAttribute);
        Assert.NotNull(services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy);
    }
    [Fact]
    public async Task Error_middleware_sanitizes_api_denial_and_revokes_expired_session()
    {
        using var f=new Fixture();var http=f.Http();
        await new LearnerSecurity(_=>throw new WebApiException(401)).InvokeAsync(http,f.Sessions);
        Assert.Equal(401,http.Response.StatusCode);Assert.Null(f.Sessions.Find(f.SessionId));
        var text=EncodingOf(http);Assert.DoesNotContain("server-only-test-token",text);
        Assert.Contains("sign in",text);
    }
    [Fact]
    public async Task Real_cookie_authentication_keeps_jwt_server_side_and_rejects_revoked_session()
    {
        using var services=RenderServices();using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        var sessions=provider.GetRequiredService<LearnerSessions>();
        var id=sessions.Create(new LearnerSession {AccessToken="secret-api-token",UserId=Guid.NewGuid(),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)});
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(LearnerSessions.Claim,id)],CookieAuthenticationDefaults.AuthenticationScheme));
        var http=new DefaultHttpContext{RequestServices=provider};http.Request.Scheme="https";
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,principal);
        var cookie=http.Response.Headers.SetCookie.ToString();
        Assert.Contains("secure",cookie);Assert.Contains("httponly",cookie);Assert.Contains("samesite=strict",cookie);
        Assert.DoesNotContain("secret-api-token",cookie);
        var cookiePair=cookie.Split(';')[0];
        using var nextScope=services.CreateScope();var next=new DefaultHttpContext{RequestServices=nextScope.ServiceProvider};next.Request.Scheme="https";next.Request.Headers.Cookie=cookiePair;
        var authenticated=await next.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.True(authenticated.Succeeded);Assert.DoesNotContain(authenticated.Principal!.Claims,x=>x.Value=="secret-api-token");
        sessions.Remove(id);
        using var lastScope=services.CreateScope();var last=new DefaultHttpContext{RequestServices=lastScope.ServiceProvider};last.Request.Scheme="https";last.Request.Headers.Cookie=cookiePair;
        Assert.False((await last.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Succeeded);
    }
    [Fact]
    public async Task Antiforgery_rejects_missing_token_and_accepts_bound_cookie_and_form_header()
    {
        using var services=RenderServices();using var scope=services.CreateScope();
        var csrf=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var get=new DefaultHttpContext {RequestServices=scope.ServiceProvider};get.Request.Scheme="https";
        var tokens=csrf.GetAndStoreTokens(get);
        var bad=new DefaultHttpContext {RequestServices=scope.ServiceProvider};bad.Request.Scheme="https";bad.Request.Method="POST";
        await Assert.ThrowsAsync<Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException>(()=>csrf.ValidateRequestAsync(bad));
        var good=new DefaultHttpContext {RequestServices=scope.ServiceProvider};good.Request.Scheme="https";good.Request.Method="POST";
        good.Request.Headers.Cookie=get.Response.Headers.SetCookie.ToString().Split(';')[0];good.Request.Headers[tokens.HeaderName!]=tokens.RequestToken;
        await csrf.ValidateRequestAsync(good);
    }
    private static string EncodingOf(HttpContext http)=>System.Text.Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());
    internal static ServiceProvider RenderServices(bool sessionValid=true)
    {
        var services=new ServiceCollection();var environment=new WebEnvironment();
        services.AddSingleton<IWebHostEnvironment>(environment);services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();services.AddSingleton<System.Diagnostics.DiagnosticListener>(new System.Diagnostics.DiagnosticListener("tests"));
        services.AddSingleton<System.Diagnostics.DiagnosticSource>(sp=>sp.GetRequiredService<System.Diagnostics.DiagnosticListener>());
        services.AddLearnerWeb(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {{"LmsApi:BaseUrl","https://api.example.test/"}}).Build());
        services.AddHttpClient<LearnerApi>().ConfigurePrimaryHttpMessageHandler(()=>new SessionValidationHandler(sessionValid));
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        return services.BuildServiceProvider();
    }
    private sealed class SessionValidationHandler(bool valid) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            // New stored-stamp validation is exercised through an in-memory API response.
            // Never permit this rendering/authentication fixture to make network requests.
            Assert.Equal("/api/account/session",request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(valid?System.Net.HttpStatusCode.OK:System.Net.HttpStatusCode.Unauthorized));
        }
    }
    private static async Task<string> Render(ServiceProvider services,UnitPage model)
    {
        using var scope=services.CreateScope();var http=new DefaultHttpContext {RequestServices=scope.ServiceProvider};
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());
        var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var found=engine.GetView(null,"/Views/Learn/Unit.cshtml",false);
        Assert.True(found.Success,string.Join(",",found.SearchedLocations ?? []));
        using var writer=new StringWriter();
        var data=new ViewDataDictionary<UnitPage>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        var temp=new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        await found.View.RenderAsync(new ViewContext(action,found.View,data,temp,writer,new HtmlHelperOptions()));return writer.ToString();
    }
    private sealed class WebEnvironment : IWebHostEnvironment
    {
        public string ApplicationName {get;set;}=typeof(LearnController).Assembly.GetName().Name!;
        public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public string WebRootPath {get;set;}=AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    internal sealed class Clock : TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    internal sealed class Fixture : IDisposable
    {
        public Guid Org=Guid.NewGuid(),Course=Guid.NewGuid(),Section=Guid.NewGuid(),Lesson=Guid.NewGuid(),Item=Guid.NewGuid();
        public Clock Clock {get;}=new(); public LearnerSessions Sessions {get;} public string SessionId {get;}
        public LearnerSession Session {get;} public ClaimsPrincipal User {get;}
        public Mock<IAuthenticationService> Auth {get;}=new();public Handler Handler {get;}public LearnerApi Api {get;}
        private readonly MemoryCache cache=new(new MemoryCacheOptions());private readonly HttpClient client;private readonly ServiceProvider services;
        public Fixture()
        {
            Sessions=new(cache,new EphemeralDataProtectionProvider(),Clock);
            Session=new(){AccessToken="server-only-test-token",UserId=Guid.NewGuid(),OrganizationId=Org,OrganizationRole="Student",ExpiresAt=Clock.Now.AddMinutes(30)};
            SessionId=Sessions.Create(Session);User=new(new ClaimsIdentity([new Claim(LearnerSessions.Claim,SessionId)],"test"));
            Handler=new(this);client=new(Handler){BaseAddress=new Uri("https://api.example.test/")};Api=new(client);
            Auth.Setup(x=>x.SignOutAsync(It.IsAny<HttpContext>(),It.IsAny<string>(),It.IsAny<AuthenticationProperties>())).Returns(Task.CompletedTask);
            services=new ServiceCollection().AddSingleton(Auth.Object).BuildServiceProvider();
        }
        public DefaultHttpContext Http(){var h=new DefaultHttpContext {User=User,RequestServices=services};h.Response.Body=new MemoryStream();h.Request.Method="GET";return h;}
        public AccountController Account()
        {
            var http=Http();
            return new(Api,Sessions){ControllerContext=new ControllerContext{HttpContext=http},Url=Mock.Of<IUrlHelper>(),TempData=new TempDataDictionary(http,Mock.Of<ITempDataProvider>())};
        }
        public LearnController Learn()=>new(Api,Sessions){ControllerContext=new ControllerContext{HttpContext=Http()}};
        public void Dispose(){client.Dispose();cache.Dispose();services.Dispose();}
    }
    internal sealed record Sent(string Path,string Method,string? Authorization,string? Organization,string? Range,string? IfRange);
    internal sealed class Handler(Fixture f) : HttpMessageHandler
    {
        public Dictionary<string,object> Responses {get;}=[];
        public string? ValidationPath; public List<string> Bodies {get;}=[];
        public string? Role="Student";public bool Platform=false,OwnsCourse=true;public List<Sent> Requests {get;}=[];public string? FailurePath;public int Failure=403,MediaStatus=206,ItemType=3;
        public bool WrongMime;public bool EmptyItems;public LegacyView? Legacy;public CourseView[]? Courses;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Content is not null)Bodies.Add(await request.Content.ReadAsStringAsync(token));
            return await Respond(request,token);
        }
        private Task<HttpResponseMessage> Respond(HttpRequestMessage request,CancellationToken token)
        {
            var path=request.RequestUri!.AbsolutePath;
            Requests.Add(new(path,request.Method.Method,request.Headers.Authorization?.ToString(),request.Headers.TryGetValues("X-Organization-Id",out var org)?org.Single():null,request.Headers.Range?.ToString(),request.Headers.IfRange?.ToString()));
            if(path==FailurePath)return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Failure){Content=new StringContent("PRIVATE UPSTREAM ERROR")});
            if(path==ValidationPath && request.Method!=HttpMethod.Get)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=JsonContent.Create(new {errors=new {Title=new[]{"Title is invalid <script>bad</script>"}}})});
            if(request.Method==HttpMethod.Get&&Responses.TryGetValue(path,out var custom))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data=custom})});
            if(path.EndsWith("/media"))
            {
                var response=new HttpResponseMessage((HttpStatusCode)MediaStatus){Content=new ByteArrayContent(MediaStatus==206?[2,3,4]:[])};
                response.Content.Headers.ContentType=new(WrongMime?"text/html":ItemType==5?"application/pdf":"audio/mpeg");
                if(MediaStatus==206)response.Content.Headers.ContentRange=new(2,4,10);
                response.Headers.Add("Set-Cookie","must-not-forward=true");response.Headers.Add("Accept-Ranges","bytes");return Task.FromResult(response);
            }
            if(request.Method!=HttpMethod.Get && path!="/api/Auth/login")
            {
                if(path.EndsWith("/media-assets"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created){Content=JsonContent.Create(new {success=true,data=new AssetView(Guid.NewGuid(),f.Lesson,f.Org,ItemType)})});
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true})});
            }
            object? data=path switch
            {
                "/api/Auth/login"=>new ApiLogin {AccessToken=f.Session.AccessToken,UserId=f.Session.UserId,ExpiresAt=f.Session.ExpiresAt},
                "/api/organizations/me"=>new[]{new OrganizationView(f.Org,"Organization",true)},
                "/api/Enrollments/my-courses/progress"=>Array.Empty<object>(),
                "/api/Enrollments/me"=>new[]{new EnrollmentView(f.Course,1)},
                "/api/Courses"=>Courses??[new CourseView(f.Course,"Course","",true,OwnsCourse?f.Session.UserId:Guid.NewGuid())],
                _=>null
            };
            if(path==$"/api/organizations/{f.Org}/context")data=new OrganizationAccess(f.Org,f.Session.UserId,Role,Platform);
            if(path==$"/api/organizations/{f.Org}/members/{f.Session.UserId}")data=new MemberView(f.Org,f.Session.UserId,2);
            if(path==$"/api/organizations/{f.Org}/members")
            {
                var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                data=new AdministrationPage<MemberView>(f.Org,query.TryGetValue("page",out var page)?int.Parse(page.ToString()):1,
                    query.TryGetValue("pageSize",out var size)?int.Parse(size.ToString()):20,1,[new(f.Org,f.Session.UserId,2)]);
            }
            if(path==$"/api/Courses/{f.Course}/progress")data=new CurrentCourseProgress(f.Course,f.Session.UserId,1,0,0,false,1,null);
            if(path==$"/api/Courses/{f.Course}")data=new CourseView(f.Course,"Course","",true,OwnsCourse?f.Session.UserId:Guid.NewGuid());
            if(path==$"/api/Courses/{f.Course}/sections")data=new[]{new SectionView(f.Section,f.Course,"Section",0)};
            if(path==$"/api/Sections/{f.Section}/lessons")data=new[]{new LessonView(f.Lesson,f.Section,"Unit",null,true,0)};
            if(path==$"/api/Lessons/{f.Lesson}/content-items")data=new ContentView(EmptyItems?[]:[new(f.Item,f.Lesson,ItemType,"Media",0,true,null,null)],Legacy);
            return Task.FromResult(data is null?new HttpResponseMessage(HttpStatusCode.NotFound):new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data})});
        }
    }
}
