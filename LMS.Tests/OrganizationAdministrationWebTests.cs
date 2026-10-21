using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class OrganizationAdministrationWebTests
{
    private sealed class Fixture:IDisposable
    {
        internal LearnerWebTests.Fixture F=new();internal Capture Handler;internal LearnerApi Api;private HttpClient client;
        internal Fixture(){Handler=new(F);client=new(Handler){BaseAddress=new("https://api.example.test/")};Api=new(client);F.Handler.Role="Admin";}
        internal AdminController Admin(){var h=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=h},TempData=new TempDataDictionary(h,Mock.Of<ITempDataProvider>())};}
        internal PlatformController Platform(){var h=F.Http();return new(Api,F.Sessions){ControllerContext=new(){HttpContext=h},TempData=new TempDataDictionary(h,Mock.Of<ITempDataProvider>())};}
        public void Dispose(){client.Dispose();F.Dispose();}
    }
    private sealed class Capture(LearnerWebTests.Fixture f):HttpMessageHandler
    {
        private readonly HttpMessageInvoker inner=new(f.Handler,false);
        internal bool Platform=true;internal int? MutationStatus;internal string Message="Account unavailable or membership conflicts with current state.";internal bool Ambiguous;
        internal List<(string Method,string Path,string? Organization,string? Body)> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var path=request.RequestUri!.AbsolutePath;var method=request.Method.Method;
            Requests.Add((method,request.RequestUri.PathAndQuery,request.Headers.TryGetValues("X-Organization-Id",out var ids)?ids.Single():null,request.Content is null?null:await request.Content.ReadAsStringAsync(token)));
            if(path=="/api/organizations/platform/context")return new(Platform?HttpStatusCode.OK:HttpStatusCode.Forbidden){Content=JsonContent.Create(new{success=Platform,data=new PlatformAccessView(f.Session.UserId,Platform)})};
            if(path=="/api/organizations" && method=="GET")return new(HttpStatusCode.OK){Content=JsonContent.Create(new{success=true,data=new AdministrationPage<OrganizationView>(null,1,20,0,[])})};
            if(method is "POST" or "PUT")
            {
                if(Ambiguous)throw new HttpRequestException("private network diagnostic");
                if(MutationStatus.HasValue)return new((HttpStatusCode)MutationStatus.Value){Content=JsonContent.Create(new{success=false,message=Message})};
            }
            return await inner.SendAsync(request,token);
        }
    }
    [Fact] public async Task Member_lists_have_encoded_bounded_query_and_scoped_totals()
    {
        using var f=new Fixture();var page=await f.Api.Members(f.F.Session,f.F.Org,new(){Search="a&b",Role=2,PageSize=10,Page=2},default);
        Assert.Equal(2,page.Page);Assert.Equal(10,page.PageSize);Assert.Equal(1,page.Total);
        Assert.Contains(f.Handler.Requests,x=>x.Path.Contains("search=a%26b") && x.Path.Contains("role=2") && x.Path.Contains("page=2"));
        Assert.All(f.Handler.Requests,x=>{Assert.Equal("GET",x.Method);Assert.Equal(f.F.Org.ToString(),x.Organization);});
        await Assert.ThrowsAsync<WebApiException>(()=>f.Api.Members(f.F.Session,Guid.NewGuid(),new(),default));
    }
    [Theory][InlineData("",1,20)][InlineData("?page=2&pageSize=10&role=2",2,10)]
    public async Task Actual_API_query_binding_preserves_defaults_and_filters(string query,int page,int size)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var h=new DefaultHttpContext{RequestServices=scope.ServiceProvider};h.Request.QueryString=new(query);
        var type=typeof(LMS.Application.Features.Organizations.OrganizationListFilter);var metadata=scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(type);
        var info=new BindingInfo{BindingSource=BindingSource.Query};var binder=scope.ServiceProvider.GetRequiredService<IModelBinderFactory>().CreateBinder(new(){Metadata=metadata,BindingInfo=info,CacheToken=type});
        var action=new ActionContext(h,new RouteData(),new ActionDescriptor());var provider=new QueryStringValueProvider(BindingSource.Query,h.Request.Query,System.Globalization.CultureInfo.InvariantCulture);
        var context=DefaultModelBindingContext.CreateBindingContext(action,provider,metadata,info,"");context.IsTopLevelObject=true;await binder.BindModelAsync(context);
        var filter=Assert.IsType<LMS.Application.Features.Organizations.OrganizationListFilter>(context.Result.Model);Assert.Equal(page,filter.Page);Assert.Equal(size,filter.PageSize);filter.Validate(true);
    }
    [Fact] public async Task Member_payloads_use_exact_email_and_confirmation_without_user_lookup_calls()
    {
        using var f=new Fixture();await f.Api.SetMember(f.F.Session,f.F.Org,new(){Email="existing@example.test",Role=2,Confirmed=true},default);
        var request=f.Handler.Requests.Last();Assert.Equal("POST",request.Method);Assert.Equal($"/api/organizations/{f.F.Org}/members",request.Path);
        using var body=JsonDocument.Parse(request.Body!);Assert.Equal("existing@example.test",body.RootElement.GetProperty("email").GetString());Assert.True(body.RootElement.GetProperty("confirmed").GetBoolean());
        Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("/users")||x.Path.Contains("example.test"));
    }
    [Fact] public async Task Membership_confirmation_blocks_mutation_and_errors_preserve_safe_values()
    {
        using var f=new Fixture();var c=f.Admin();var form=new MemberForm {Email="existing@example.test",Role=2};
        var view=Assert.IsType<ViewResult>(await c.SetMember(f.F.Org,form,default));var page=Assert.IsType<MembersPage>(view.Model);
        Assert.Equal(form.Email,page.Form!.Email);Assert.False(page.Form.Confirmed);Assert.Equal(400,c.Response.StatusCode);Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Fact] public async Task Failed_role_change_on_later_page_preserves_filters_and_selected_role()
    {
        using var f=new Fixture();f.Handler.MutationStatus=409;var c=f.Admin();
        var view=Assert.IsType<ViewResult>(await c.SetMember(f.F.Org,new(){UserId=f.F.Session.UserId,Role=3,Confirmed=true,Page=2,PageSize=10,Search="Name",FilterRole=2},default));
        var page=Assert.IsType<MembersPage>(view.Model);Assert.Equal(2,page.Page);Assert.Equal(10,page.PageSize);Assert.Equal("Name",page.Search);Assert.Equal(2,page.Role);Assert.Equal(3,page.Form!.Role);Assert.False(page.Form.Confirmed);
    }
    [Fact] public async Task Existing_instructor_chooser_is_bounded_scoped_and_allows_later_pages()
    {
        using var f=new Fixture();var choices=await f.Api.InstructorChoices(f.F.Session,f.F.Org,f.F.Course,default,new(){Page=2,PageSize=10,Search="Name"});
        Assert.Equal(2,choices.Choices!.Page);Assert.Equal(1,choices.Choices.Total);Assert.All(choices.Instructors,x=>Assert.Equal(2,x.Role));
        await f.Api.AssignInstructor(f.F.Session,f.F.Org,f.F.Course,f.F.Session.UserId,default);Assert.Contains(f.Handler.Requests,x=>x.Method=="PUT"&&x.Path==$"/api/Courses/{f.F.Course}/instructor");
    }
    [Fact] public async Task Unselected_verified_platform_home_does_not_select_an_organization()
    {
        using var f=new Fixture();var original=f.F.Session;
        var id=f.F.Sessions.Create(new(){UserId=original.UserId,AccessToken=original.AccessToken,ExpiresAt=original.ExpiresAt});
        var h=f.F.Http();h.User=new(new ClaimsIdentity([new Claim(LearnerSessions.Claim,id)],"test"));
        var c=new AccountController(f.Api,f.F.Sessions){ControllerContext=new(){HttpContext=h},Url=Mock.Of<IUrlHelper>()};
        Assert.Equal("/platform",Assert.IsType<RedirectResult>(await c.Home(default)).Url);Assert.Null(f.F.Sessions.Require(h.User).OrganizationId);
        f.Handler.Platform=false;Assert.IsType<RedirectToActionResult>(await c.Home(default));
    }
    [Theory][InlineData("The last organization Admin cannot be demoted. Assign another Admin first.")][InlineData("Reassign all courses before changing this Instructor's role.")][InlineData("Resolve all Active enrollments before changing this Student's role.")]
    public async Task Guarded_role_conflicts_show_public_message_and_require_fresh_confirmation(string message)
    {
        using var f=new Fixture();f.Handler.MutationStatus=409;f.Handler.Message=message;var c=f.Admin();var form=new MemberForm {UserId=f.F.Session.UserId,Role=2,Confirmed=true};
        var view=Assert.IsType<ViewResult>(await c.SetMember(f.F.Org,form,default));Assert.Equal(409,c.Response.StatusCode);Assert.Contains(c.ModelState.Values.SelectMany(x=>x.Errors),x=>x.ErrorMessage==message);Assert.False(Assert.IsType<MembersPage>(view.Model).Form!.Confirmed);
        Assert.Single(f.Handler.Requests,x=>x.Method=="PUT");
    }
    [Fact] public async Task Arbitrary_upstream_diagnostics_are_never_reflected()
    {
        using var f=new Fixture();f.Handler.MutationStatus=409;f.Handler.Message="secret database diagnostic <script>";var c=f.Admin();
        await c.SetMember(f.F.Org,new(){Email="existing@example.test",Role=1,Confirmed=true},default);
        Assert.DoesNotContain(c.ModelState.Values.SelectMany(x=>x.Errors),x=>x.ErrorMessage.Contains("diagnostic"));
    }
    [Fact] public async Task Successful_membership_is_PRG_and_stale_admin_session_is_denied()
    {
        using var f=new Fixture();var c=f.Admin();Assert.Equal($"/admin/{f.F.Org}/members",Assert.IsType<RedirectResult>(await c.SetMember(f.F.Org,new(){Email="existing@example.test",Role=1,Confirmed=true},default)).Url);
        Assert.Contains("saved",c.TempData["MemberNotice"]!.ToString());
        f.F.Handler.Role="Student";f.Handler.Requests.Clear();c=f.Admin();
        var stale=f.F.Sessions.Create(new(){AccessToken=f.F.Session.AccessToken,UserId=f.F.Session.UserId,ExpiresAt=f.F.Session.ExpiresAt,OrganizationId=f.F.Org,OrganizationRole="Admin"});
        c.HttpContext.User=new(new ClaimsIdentity([new Claim(LearnerSessions.Claim,stale),new Claim(ClaimTypes.Role,"Admin")],"test"));
        Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>c.SetMember(f.F.Org,new(){Email="existing@example.test",Role=3,Confirmed=true},default))).Status);Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Ambiguous_changes_redirect_to_read_only_recovery_without_replay(bool organization)
    {
        using var f=new Fixture();f.Handler.Ambiguous=true;
        if(organization){var c=f.Platform();Assert.Equal("/platform/organizations",Assert.IsType<RedirectResult>(await c.Create(new(){Code="CODE",Name="Name",InitialAdminEmail="admin@example.test",Confirmed=true},default)).Url);Assert.Contains("unknown",c.TempData["OrganizationNotice"]!.ToString());}
        else {var c=f.Admin();Assert.Equal($"/admin/{f.F.Org}/members",Assert.IsType<RedirectResult>(await c.SetMember(f.F.Org,new(){Email="existing@example.test",Role=2,Confirmed=true},default)).Url);Assert.Contains("unknown",c.TempData["MemberNotice"]!.ToString());}
        Assert.Single(f.Handler.Requests,x=>x.Method=="POST");
    }
    [Fact] public async Task Platform_pages_are_stored_authorized_without_selected_org_header()
    {
        using var f=new Fixture();await f.Platform().Index(default);await f.Platform().Organizations(new(),default);await f.Platform().Create(default);
        Assert.All(f.Handler.Requests,x=>{Assert.Equal("GET",x.Method);Assert.Null(x.Organization);});
        f.Handler.Platform=false;Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>f.Platform().Index(default))).Status);
        Assert.NotNull(f.F.Sessions.Find(f.F.SessionId));
    }
    [Fact] public async Task Platform_creation_is_confirmed_PRG_and_does_not_select_org()
    {
        using var f=new Fixture();var c=f.Platform();var form=new OrganizationCreateForm{Code="CODE",Name="Name",InitialAdminEmail="admin@example.test"};
        Assert.IsType<ViewResult>(await c.Create(form,default));Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
        c=f.Platform();form.Confirmed=true;Assert.Equal("/platform/organizations",Assert.IsType<RedirectResult>(await c.Create(form,default)).Url);
        Assert.Null(f.Handler.Requests.Single(x=>x.Method=="POST").Organization);Assert.Equal(f.F.Org,f.F.Sessions.Find(f.F.SessionId)!.OrganizationId);Assert.DoesNotContain(f.Handler.Requests,x=>x.Path.Contains("/context")&&x.Path.Contains(f.F.Org.ToString()));
    }
    [Fact] public async Task Platform_conflicts_preserve_values_and_revoked_superadmin_cannot_create()
    {
        using var f=new Fixture();f.Handler.MutationStatus=409;var c=f.Platform();var form=new OrganizationCreateForm{Code="CODE",Name="<script>Name</script>",InitialAdminEmail="admin@example.test",Confirmed=true};
        var view=Assert.IsType<ViewResult>(await c.Create(form,default));Assert.Equal(form.Name,Assert.IsType<OrganizationCreateForm>(view.Model).Name);Assert.False(form.Confirmed);Assert.Equal(409,c.Response.StatusCode);
        f.Handler.Platform=false;f.Handler.Requests.Clear();await Assert.ThrowsAsync<WebApiException>(()=>f.Platform().Create(form,default));Assert.All(f.Handler.Requests,x=>Assert.Equal("GET",x.Method));
    }
    [Fact] public async Task Safe_rendering_includes_antiforgery_and_no_tokens_or_directory()
    {
        using var f=new Fixture();var member=new MemberView(f.F.Org,f.F.Session.UserId,2,"<script>Name</script>","Last","name@example.test");
        var html=await Render("Admin/Members",new MembersPage(f.F.Org,[member],1,20,1,"<script>search</script>",null,new(){Email="safe@example.test",Role=1}));
        Assert.Contains("&lt;script&gt;Name",html);Assert.Contains("&lt;script&gt;search",html);Assert.Contains("__RequestVerificationToken",html);Assert.Contains("name=\"Confirmed\"",html);Assert.Contains("safe@example.test",html);Assert.DoesNotContain(f.F.Session.AccessToken,html);Assert.DoesNotContain("<script>Name",html);
        html=await Render("Platform/Create",new OrganizationCreateForm {Code="CODE",Name="<script>Org</script>",InitialAdminEmail="admin@example.test"});Assert.Contains("&lt;script&gt;Org",html);Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain("Password",html);
        html=await Render("Platform/Organizations",new PlatformOrganizationsPage(new(null,1,20,1,[new(Guid.NewGuid(),"<script>Org</script>",true,"<script>Code</script>")]),new()));Assert.Contains("&lt;script&gt;Code",html);Assert.Contains("Explicitly select",html);Assert.Contains("__RequestVerificationToken",html);
        html=await Render("Platform/Organizations",new PlatformOrganizationsPage(new(null,1,20,0,[]),new()));Assert.Contains("No matching organizations",html);
    }
    [Fact] public async Task Mutation_filters_and_actual_antiforgery_reject_missing_tokens()
    {
        foreach(var type in new[]{typeof(AdminController),typeof(PlatformController)})Assert.True(Attribute.IsDefined(type,typeof(AutoValidateAntiforgeryTokenAttribute)));
        Assert.NotNull(typeof(AdminController).GetMethod(nameof(AdminController.SetMember))!.GetCustomAttribute<EnableRateLimitingAttribute>());
        Assert.NotNull(typeof(PlatformController).GetMethods().Single(x=>x.Name=="Create"&&x.GetCustomAttribute<HttpPostAttribute>() is not null).GetCustomAttribute<EnableRateLimitingAttribute>());
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var csrf=scope.ServiceProvider.GetRequiredService<IAntiforgery>();var h=new DefaultHttpContext{RequestServices=scope.ServiceProvider};h.Request.Scheme="https";h.Request.Method="POST";
        await Assert.ThrowsAsync<AntiforgeryValidationException>(()=>csrf.ValidateRequestAsync(h));
    }
    [Fact] public async Task Expired_sessions_and_forged_platform_claims_do_not_grant_access()
    {
        using var f=new Fixture();((ClaimsIdentity)f.F.User.Identity!).AddClaim(new Claim(ClaimTypes.Role,"SuperAdmin"));f.Handler.Platform=false;
        await Assert.ThrowsAsync<WebApiException>(()=>f.Platform().Index(default));f.F.Clock.Now=f.F.Session.ExpiresAt.AddSeconds(1);Assert.Equal(401,(await Assert.ThrowsAsync<WebApiException>(()=>f.Platform().Organizations(new(),default))).Status);
    }
    private static async Task<string> Render(string path,object model)
    {
        using var services=LearnerWebTests.RenderServices();using var scope=services.CreateScope();var h=new DefaultHttpContext{RequestServices=scope.ServiceProvider};h.Request.Scheme="https";
        var action=new ActionContext(h,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var found=engine.GetView(null,$"/Views/{path}.cshtml",false);Assert.True(found.Success);
        using var writer=new StringWriter();var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
        await found.View.RenderAsync(new ViewContext(action,found.View,data,new TempDataDictionary(h,scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
