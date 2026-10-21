using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using LMS.Web.Controllers;
using LMS.Web.Models;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace LMS.Tests;

public sealed class AuditWebTests
{
    private sealed class Capture(LearnerWebTests.Fixture fixture) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker inner=new(fixture.Handler,false);
        public readonly List<(string Method,string Path,string? Org)> Requests=[];
        public bool Platform=true;public bool Foreign;public bool Unauthorized;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var path=request.RequestUri!.AbsolutePath;Requests.Add((request.Method.Method,request.RequestUri.PathAndQuery,request.Headers.TryGetValues("X-Organization-Id",out var org)?org.Single():null));
            if(Unauthorized)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            if(path=="/api/organizations/platform/context")return Task.FromResult(new HttpResponseMessage(Platform?HttpStatusCode.OK:HttpStatusCode.Forbidden){Content=JsonContent.Create(new{success=Platform,data=new PlatformAccessView(fixture.Session.UserId,Platform)})});
            if(path.StartsWith("/api/audit/"))
            {
                var organization=path.Contains("/organizations/")?(Foreign?Guid.NewGuid():fixture.Org):(Guid?)null;
                var query=QueryHelpers.ParseQuery(request.RequestUri.Query);
                var page=query.TryGetValue("page",out var p)?int.Parse(p.ToString()):1;var size=query.TryGetValue("pageSize",out var s)?int.Parse(s.ToString()):20;
                var filter=new AuditViewFilter {Page=page,PageSize=size,FromUtc=DateTimeOffset.UtcNow.AddDays(-7),ToUtc=DateTimeOffset.UtcNow};
                var data=new AuditViewPage(organization,page,size,0,filter,[]);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{success=true,data})});
            }
            return inner.SendAsync(request,token);
        }
    }
    [Fact] public async Task Viewer_reads_only_GETs_with_selected_tenant_and_preserves_bounded_filters()
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role="Admin";var capture=new Capture(f);using var client=new HttpClient(capture){BaseAddress=new("https://api.example.test/")};
        var api=new LearnerApi(client);var filter=new AuditViewFilter {Page=2,PageSize=10,Action="membership.role-changed",Outcome="succeeded",ActorId=Guid.NewGuid(),ResourceId=Guid.NewGuid()};
        var result=await api.AuditPage(f.Session,f.Org,filter,default);Assert.Equal(2,result.Page);Assert.Equal(10,result.PageSize);
        Assert.All(capture.Requests,r=>{Assert.Equal("GET",r.Method);Assert.Equal(f.Org.ToString(),r.Org);});
        Assert.Contains(capture.Requests,r=>r.Path.Contains("action=membership.role-changed")&&r.Path.Contains("outcome=succeeded")&&r.Path.Contains("actorId=")&&r.Path.Contains("resourceId="));
        await Assert.ThrowsAsync<WebApiException>(()=>api.AuditPage(f.Session,Guid.NewGuid(),new(),default));
        capture.Foreign=true;Assert.Equal(502,(await Assert.ThrowsAsync<WebApiException>(()=>api.AuditPage(f.Session,f.Org,new(),default))).Status);
    }
    [Theory][InlineData("Student")][InlineData("Instructor")]
    public async Task Stored_staff_role_does_not_grant_admin_audit_view(string role)
    {
        using var f=new LearnerWebTests.Fixture();f.Handler.Role=role;var capture=new Capture(f);using var client=new HttpClient(capture){BaseAddress=new("https://api.example.test/")};
        Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>new LearnerApi(client).AuditPage(f.Session,f.Org,new(),default))).Status);
        Assert.DoesNotContain(capture.Requests,r=>r.Path.StartsWith("/api/audit/"));
    }
    [Fact] public async Task Platform_view_has_no_org_header_no_all_org_query_and_requires_stored_platform_access()
    {
        using var f=new LearnerWebTests.Fixture();var capture=new Capture(f);using var client=new HttpClient(capture){BaseAddress=new("https://api.example.test/")};var api=new LearnerApi(client);
        await api.AuditPage(f.Session,null,new(),default);Assert.All(capture.Requests,r=>{Assert.Equal("GET",r.Method);Assert.Null(r.Org);});
        Assert.Contains(capture.Requests,r=>r.Path.StartsWith("/api/audit/platform"));Assert.DoesNotContain(capture.Requests,r=>r.Path.Contains("/audit/organizations"));
        capture.Platform=false;Assert.Equal(403,(await Assert.ThrowsAsync<WebApiException>(()=>api.AuditPage(f.Session,null,new(),default))).Status);
    }
    [Fact] public async Task Expired_and_API_rejected_sessions_fail_before_viewing_history()
    {
        using var f=new LearnerWebTests.Fixture();var capture=new Capture(f){Unauthorized=true};using var client=new HttpClient(capture){BaseAddress=new("https://api.example.test/")};var api=new LearnerApi(client);
        var controller=new LMS.Web.Controllers.AuditController(api,f.Sessions){ControllerContext=new(){HttpContext=f.Http()}};
        Assert.Equal(401,(await Assert.ThrowsAsync<WebApiException>(()=>controller.Platform(new(),default))).Status);
        f.Sessions.Remove(f.SessionId);var count=capture.Requests.Count;
        Assert.Equal(401,(await Assert.ThrowsAsync<WebApiException>(()=>controller.Platform(new(),default))).Status);Assert.Equal(count,capture.Requests.Count);
    }
    [Fact] public void MVC_actions_are_authorized_and_read_only()
    {
        var type=typeof(LMS.Web.Controllers.AuditController);Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        foreach(var name in new[]{"Organization","OrganizationDetail","Platform","PlatformDetail"})
        {var method=type.GetMethod(name)!;Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());}
    }
    [Theory][InlineData("Index")][InlineData("Detail")]
    public async Task Actual_Razor_encodes_metadata_and_fields_and_provides_no_write_or_export_controls(string view)
    {
        using var services=LearnerWebTests.RenderServices();var row=new AuditViewRow(Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),Guid.NewGuid(),"organization",
            "<script>action</script>","<script>resource</script>",Guid.NewGuid(),"<script>outcome</script>","<script>correlation</script>","<script>metadata</script>");
        var filter=new AuditViewFilter {FromUtc=DateTimeOffset.UtcNow.AddDays(-7),ToUtc=DateTimeOffset.UtcNow};
        var html=view=="Detail"?await Render(services,"/Views/Audit/Detail.cshtml",row):await Render(services,"/Views/Audit/Index.cshtml",new AuditViewPage(row.OrganizationId,1,20,1,filter,[row]));
        Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;metadata&lt;/script&gt;",html);Assert.Contains("&lt;script&gt;action&lt;/script&gt;",html);
        Assert.DoesNotContain("method=\"post\"",html);Assert.DoesNotContain("Export",html);Assert.DoesNotContain("Delete",html);
    }
    [Fact] public async Task Empty_Razor_result_is_clear_and_pagination_preserves_fixed_date_range()
    {
        using var services=LearnerWebTests.RenderServices();var f=new AuditViewFilter {FromUtc=DateTimeOffset.UtcNow.AddDays(-7),ToUtc=DateTimeOffset.UtcNow};
        var html=await Render(services,"/Views/Audit/Index.cshtml",new AuditViewPage(null,1,20,0,f,[]));Assert.Contains("No audit events match",html);Assert.Contains("Platform and account",html);
        html=await Render(services,"/Views/Audit/Index.cshtml",new AuditViewPage(null,2,20,100,f,[]));Assert.Contains("page=1",html);Assert.Contains("page=3",html);Assert.Contains("fromUtc=",html);Assert.Contains("toUtc=",html);
    }
    private static async Task<string> Render<T>(ServiceProvider services,string path,T model)
    {
        using var scope=services.CreateScope();var http=new DefaultHttpContext {RequestServices=scope.ServiceProvider};http.Request.Scheme="https";
        var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var engine=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();var found=engine.GetView(null,path,false);
        Assert.True(found.Success,string.Join(",",found.SearchedLocations??[]));using var writer=new StringWriter();
        var data=new ViewDataDictionary<T>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};data["BasePath"]="/platform/audit";
        var temp=new TempDataDictionary(http,scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        await found.View.RenderAsync(new ViewContext(action,found.View,data,temp,writer,new HtmlHelperOptions()));return writer.ToString();
    }
}
