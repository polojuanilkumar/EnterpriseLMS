using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LMS.Web.Models;
using Microsoft.AspNetCore.Builder;
using Xunit;
using static LMS.Tests.Integration.BffAuthTests;
namespace LMS.Tests.Integration;
public sealed class BffStaffTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly InProcessMvcTests.FakeApi Api=new(){CurrentRole="Instructor"};
        public WebApplication App=null!; public InProcessServer Server=null!; public Browser Browser=null!;
        public bool Owns=true,WrongSection,WrongLesson,WrongItem,WrongAsset,Ambiguous;
        public int? Fail; public int MediaStatus=200; public string MediaMime="application/pdf"; public bool ForeignMember;
        public readonly Guid Asset=Guid.NewGuid();
        public readonly List<(string Path,string Method,string Body)> Writes=[];
        public string Root=>"/bff/staff/courses";
        public string Course=>Root+$"/{Api.Course}";
        public string Section=>Course+$"/sections/{Api.Section}";
        public string Unit=>Section+$"/lessons/{Api.Lesson}";
        public static async Task<Fixture> Create()
        {
            var f=new Fixture();f.Api.StudentResponse=f.Respond;(f.App,f.Server)=await InProcessMvcTests.Host(f.Api);
            f.Browser=new(f.Server);f.Browser.SetAuth(InProcessMvcTests.AuthCookie(f.App,f.Api,"Student"));return f;
        }
        private static HttpResponseMessage Data(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new {success=true,data=value})};
        private HttpResponseMessage? Respond(HttpRequestMessage request)
        {
            var path=request.RequestUri!.AbsolutePath;
            Assert.Equal(Api.Organization.ToString(),Assert.Single(request.Headers.GetValues("X-Organization-Id")));
            Assert.Equal(Api.AccessToken,request.Headers.Authorization!.Parameter);
            if(request.Method!=HttpMethod.Get&&request.Method!=HttpMethod.Head)
            {
                Writes.Add((path,request.Method.Method,request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()??""));
                if(Ambiguous)throw new HttpRequestException("PRIVATE uncertain write");
                if(Fail.HasValue)return new((HttpStatusCode)Fail.Value){Content=JsonContent.Create(new {message="PRIVATE diagnostic"})};
                if(path.EndsWith("/media-assets"))return Data(new AssetView(Asset,WrongAsset?Guid.NewGuid():Api.Lesson,Api.Organization,5));
                return Data(new {});
            }
            var course=new CourseView(Api.Course,"<script>Course</script>","Body",false,Owns?Api.User:Guid.NewGuid(),Status:"Draft");
            if(path=="/api/Courses")return Data(new[]{course});
            if(path==$"/api/Courses/{Api.Course}")return Data(new CourseDetail(Api.Course,course.Title,"CODE","Body",null,course.InstructorId,"Advanced","Draft",5,null));
            if(path==$"/api/Courses/{Api.Course}/sections")return Data(new[]{new SectionView(Api.Section,WrongSection?Guid.NewGuid():Api.Course,"Section",1)});
            if(path==$"/api/Courses/{Api.Course}/sections/{Api.Section}")return Data(new SectionDetail(Api.Section,Api.Course,"Section","Body",1));
            if(path==$"/api/Sections/{Api.Section}/lessons")return Data(new[]{new LessonView(Api.Lesson,WrongLesson?Guid.NewGuid():Api.Section,"Lesson",null,false,1)});
            if(path==$"/api/Lessons/{Api.Lesson}")return Data(new LessonDetail(Api.Lesson,Api.Section,"Lesson",null,"Video","<script>legacy</script>","https://evil.test/x",5,1));
            if(path==$"/api/Lessons/{Api.Lesson}/content-items")return Data(new ContentView([new ItemView(Api.Item,WrongItem?Guid.NewGuid():Api.Lesson,5,"PDF",0,false,null,null,Asset)],null));
            if(path==$"/api/Lessons/{Api.Lesson}/content-items/{Api.Item}/media")
            {
                var response=new HttpResponseMessage((HttpStatusCode)MediaStatus){Content=new ByteArrayContent([1,2,3])};response.Content.Headers.ContentType=new(MediaMime);response.Headers.AcceptRanges.Add("bytes");
                if(MediaStatus==206){Assert.Equal("bytes=0-2",request.Headers.Range!.ToString());response.Content.Headers.ContentRange=new(0,2,10);}
                if(MediaStatus==416)response.Content.Headers.ContentRange=new(10);
                return response;
            }
            if(path==$"/api/organizations/{Api.Organization}/members")
            {
                Assert.Contains("role=2",request.RequestUri.Query);Assert.Contains("pageSize=50",request.RequestUri.Query);
                return Data(new AdministrationPage<MemberView>(Api.Organization,1,50,1,[new MemberView(ForeignMember?Guid.NewGuid():Api.Organization,Api.User,2,"Instructor","Name","test@example.test")]));
            }
            if(path==$"/api/organizations/{Api.Organization}/members/{Api.User}")return Data(new MemberView(ForeignMember?Guid.NewGuid():Api.Organization,Api.User,2));
            if(path=="/api/Categories")return Data(new[]{new CategoryChoice(Guid.NewGuid(),"Category",true)});
            if(path==$"/{Api.Lesson}/quiz")return new(HttpStatusCode.NotFound);
            return null;
        }
        public Task<InProcessServer.Response> Change(string url,object body,string method="POST")=>Send(url,body,method);
        private async Task<InProcessServer.Response> Send(string url,object body,string method)=>await Browser.Send(url,method,body,await Browser.Csrf());
        public async ValueTask DisposeAsync(){await App.DisposeAsync();Api.Dispose();}
    }
    [Theory][InlineData("Instructor",false)][InlineData("Admin",false)][InlineData("Student",true)]
    public async Task Live_staff_scope_allows_reads_and_preserves_legacy_editor(string role,bool platform)
    {
        await using var f=await Fixture.Create();f.Api.CurrentRole=role;f.Api.Platform=platform;
        foreach(var path in new[]{f.Root,f.Course,f.Section,f.Unit,f.Course+"/edit",f.Section+"/edit",f.Unit+"/edit",f.Course+"/readiness"})
        {var response=await f.Browser.Send(path);Assert.Equal(200,response.Status);Assert.DoesNotContain(f.Api.AccessToken,response.Body);Assert.Contains("no-store",response.Headers.CacheControl.ToString());}
        var editor=Json(await f.Browser.Send(f.Unit+"/edit")).GetProperty("form");Assert.Equal("<script>legacy</script>",editor.GetProperty("content").GetString());Assert.Equal("https://evil.test/x",editor.GetProperty("videoUrl").GetString());Assert.Empty(f.Writes);
    }
    [Theory][InlineData("student")][InlineData("owner")][InlineData("tenant")][InlineData("section")][InlineData("lesson")][InlineData("item")][InlineData("revoked")][InlineData("context")]
    public async Task Denials_prevent_nested_mutations(string denial)
    {
        await using var f=await Fixture.Create();
        switch(denial){case "student":f.Api.CurrentRole="Student";break;case "owner":f.Owns=false;break;case "tenant":f.Api.OrganizationDenied=true;break;case "section":f.WrongSection=true;break;case "lesson":f.WrongLesson=true;break;case "item":f.WrongItem=true;break;case "revoked":f.Api.Revoked=true;break;case "context":f.Api.ContextMismatch=true;break;}
        var response=await f.Change(f.Unit+"/items/"+f.Api.Item+"/delete",new {confirmed=true});Assert.Contains(response.Status,new[]{401,403,404});Assert.Empty(f.Writes);
    }
    [Fact] public async Task Csrf_confirmations_validation_and_reorder_are_enforced()
    {
        await using var f=await Fixture.Create();
        Assert.Equal(400,(await f.Browser.Send(f.Course+"/publication","POST",new {confirmed=true,action="publish"})).Status);
        Assert.Equal(400,(await f.Change(f.Course+"/publication",new {confirmed=false,action="publish"})).Status);
        Assert.Equal(400,(await f.Change(f.Unit+"/items/"+f.Api.Item+"/delete",new {confirmed=false})).Status);
        Assert.Equal(400,(await f.Change(f.Root,new {title="",code="X",description="Body"})).Status);
        Assert.Equal(400,(await f.Change(f.Unit+"/items/reorder",new {itemIds=new[]{Guid.NewGuid()}},"PUT")).Status);
        Assert.Empty(f.Writes);
        Assert.Equal(204,(await f.Change(f.Unit+"/items/reorder",new {itemIds=new[]{f.Api.Item}},"PUT")).Status);
        Assert.Equal(204,(await f.Change(f.Course+"/publication",new {confirmed=true,action="publish"})).Status);
        Assert.Equal(204,(await f.Change(f.Unit+"/publication",new {confirmed=true,action="unpublish"})).Status);
        Assert.Equal(new[]{"PUT","POST","PATCH"},f.Writes.Select(x=>x.Method));
    }
    [Fact] public async Task Hierarchy_and_content_writes_use_existing_typed_contracts()
    {
        await using var f=await Fixture.Create();
        Assert.Equal(204,(await f.Change(f.Root,new {title="Title",code="CODE",description="Body",instructorId=Guid.NewGuid(),level=2,durationInMinutes=5})).Status);
        Assert.Equal(f.Api.User,JsonDocument.Parse(f.Writes.Last().Body).RootElement.GetProperty("instructorId").GetGuid());
        Assert.Equal(204,(await f.Change(f.Course+"/sections",new {title="Section",displayOrder=2})).Status);
        Assert.Equal(204,(await f.Change(f.Section+"/lessons",new {title="Lesson",displayOrder=2,lessonType=2,content="Legacy",videoUrl="https://legacy.test"})).Status);
        foreach(var type in Enumerable.Range(1,5))Assert.Equal(204,(await f.Change(f.Unit+"/items",new {title="Item",type,displayOrder=0,isRequired=true,text=type==1?"<script>text</script>":null,youTubeUrl=type==2?"https://youtu.be/dQw4w9WgXcQ":null,assetId=type>=3?(Guid?)f.Asset:null})).Status);
        Assert.Equal(8,f.Writes.Count);Assert.DoesNotContain(f.Writes,x=>x.Path.Contains("progress")||x.Path.Contains("quiz"));
    }
    [Theory][InlineData(400)][InlineData(409)][InlineData(413)][InlineData(500)]
    public async Task Failures_remain_sanitized_and_never_replay(int status)
    {
        await using var f=await Fixture.Create();f.Fail=status;
        var response=await f.Change(f.Course,new {title="Title",code="CODE",description="Body"},"PUT");Assert.Equal(status==500?502:status,response.Status);Assert.DoesNotContain("PRIVATE",response.Body);Assert.Single(f.Writes);
        await f.Browser.Send(f.Course);Assert.Single(f.Writes);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Multipart_csrf_and_scoped_asset_response_are_checked(bool wrong)
    {
        await using var f=await Fixture.Create();f.WrongAsset=wrong;
        using var body=new MultipartFormDataContent();body.Add(new StringContent("5"),"type");var file=new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 synthetic"));file.Headers.ContentType=new("application/pdf");body.Add(file,"file","test.pdf");
        var bytes=await body.ReadAsByteArrayAsync();var mime=body.Headers.ContentType!.ToString();
        Assert.Equal(400,(await f.Server.Send(f.Unit+"/uploads","POST",f.Browser.Cookie,rawBody:bytes,contentType:mime)).Status);Assert.Empty(f.Writes);
        var csrf=await f.Browser.Csrf();var response=await f.Server.Send(f.Unit+"/uploads","POST",f.Browser.Cookie,csrf:csrf,rawBody:bytes,contentType:mime);
        Assert.Equal(wrong?400:200,response.Status);Assert.Single(f.Writes);Assert.EndsWith("/media-assets",f.Writes[0].Path);
        if(!wrong){Assert.Equal(f.Asset,Json(response).GetProperty("id").GetGuid());Assert.DoesNotContain("path",response.Body,StringComparison.OrdinalIgnoreCase);}
    }
    [Fact] public async Task Ambiguous_writes_are_not_replayed_by_BFF()
    {
        await using var f=await Fixture.Create();f.Ambiguous=true;
        var r=await f.Change(f.Course+"/publication",new {confirmed=true,action="publish"});Assert.Equal(502,r.Status);Assert.Single(f.Writes);Assert.DoesNotContain("PRIVATE",r.Body);
        await f.Browser.Send(f.Course);Assert.Single(f.Writes);
    }
    [Theory][InlineData(206)][InlineData(416)]
    public async Task Item_media_preserves_ranges_and_safe_generated_headers(int status)
    {
        await using var f=await Fixture.Create();f.MediaStatus=status;
        var r=await f.Server.Send(f.Unit+"/items/"+f.Api.Item+"/media?download=true",cookie:f.Browser.Cookie,range:"bytes=0-2");
        Assert.Equal(status,r.Status);Assert.Contains(status==206?"bytes 0-2/10":"bytes */10",r.Headers.ContentRange.ToString());Assert.Equal("bytes",r.Headers.AcceptRanges.ToString());Assert.Contains("no-store",r.Headers.CacheControl.ToString());
        if(status==206){Assert.Contains("attachment",r.Headers.ContentDisposition.ToString());Assert.DoesNotContain("test.pdf",r.Headers.ContentDisposition.ToString());}
    }
    [Fact] public async Task Admin_instructor_choices_are_bounded_and_stored_membership_is_revalidated()
    {
        await using var f=await Fixture.Create();f.Api.CurrentRole="Admin";
        Assert.Equal(200,(await f.Browser.Send(f.Root+"/new?Page=1&PageSize=50&Search=Instructor")).Status);
        Assert.Equal(400,(await f.Browser.Send(f.Root+"/new?Page=0&PageSize=51")).Status);
        Assert.Equal(204,(await f.Change(f.Root,new {title="Title",code="CODE",description="Body",instructorId=f.Api.User})).Status);
        f.ForeignMember=true;Assert.Equal(400,(await f.Change(f.Root,new {title="Title",code="CODE",description="Body",instructorId=f.Api.User})).Status);Assert.Single(f.Writes);
        Assert.Equal(502,(await f.Browser.Send(f.Root+"/new?Page=1&PageSize=50")).Status);
    }
    [Fact] public async Task Unselected_platform_session_cannot_use_browser_organization_authority()
    {
        await using var f=await Fixture.Create();f.Api.Platform=true;
        var login=await f.Browser.Send("/bff/login","POST",new {email="user@example.test",password="password"},await f.Browser.Csrf());Assert.Equal(200,login.Status);
        Assert.Equal(403,(await f.Server.Send(f.Root+"?organizationId="+f.Api.Organization,cookie:f.Browser.Cookie,organizationHeader:f.Api.Organization.ToString())).Status);
        Assert.Empty(f.Writes);
    }
    [Fact] public async Task Media_get_head_and_revocation_reuse_private_proxy()
    {
        await using var f=await Fixture.Create();var url=f.Unit+"/items/"+f.Api.Item+"/media";
        foreach(var method in new[]{"GET","HEAD"}){var r=await f.Browser.Send(url,method);Assert.Equal(200,r.Status);Assert.Equal("application/pdf",r.Headers.ContentType.ToString());Assert.Contains("no-store",r.Headers.CacheControl.ToString());Assert.Equal("nosniff",r.Headers["X-Content-Type-Options"].ToString());}
        f.Owns=false;Assert.Equal(403,(await f.Browser.Send(url)).Status);Assert.Empty(f.Writes);
    }
}
