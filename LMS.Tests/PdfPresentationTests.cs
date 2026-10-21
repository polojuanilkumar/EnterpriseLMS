using System.Text;
using LMS.Api.Controllers;
using LMS.Application.Features.LessonContent;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class PdfPresentationTests
{
    // A minimal one-page PDF with correct byte offsets; not a browser rendering test.
    private static byte[] Pdf(string version = "1.7")
    {
        var document = new StringBuilder($"%PDF-{version}\n");
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << >> >>"];
        var offsets = new List<int>();
        for (var i=0;i<objects.Length;i++) { offsets.Add(document.Length);document.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = document.Length;
        document.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets) document.Append($"{offset:D10} 00000 n \n");
        document.Append($"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(document.ToString());
    }
    [Theory]
    [InlineData("deck.pdf","application/pdf",1,true)]
    [InlineData("deck.PDF","application/pdf",26214400,true)]
    [InlineData("deck.pdf","application/pdf",26214401,false)]
    [InlineData("deck.pdf","application/pdf",0,false)]
    [InlineData("deck.pdf","application/octet-stream",100,false)]
    [InlineData("deck.pdf","video/mp4",100,false)]
    [InlineData("deck.pdf.exe","application/pdf",100,false)]
    [InlineData("deck.pptx","application/vnd.openxmlformats-officedocument.presentationml.presentation",100,false)]
    public void Declaration_enforces_pdf_contract(string name,string mime,long size,bool allowed)
    {
        var error = Record.Exception(() => MediaValidation.CheckDeclaration(name,mime,size));
        if (allowed) Assert.Null(error); else Assert.IsType<ArgumentException>(error);
    }
    [Theory]
    [InlineData("1.0")] [InlineData("1.7")] [InlineData("2.0")]
    public async Task Supported_signatures_reset_stream(string version)
    {
        using var stream=new MemoryStream(Pdf(version));
        await MediaValidation.CheckSignatureAsync(stream,LessonContentType.Pdf,default);
        Assert.Equal(0,stream.Position);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Spoofed_truncated_or_unsupported_pdf_is_rejected(int scenario)
    {
        var bytes = scenario switch
        {
            0 => Encoding.ASCII.GetBytes("<html>not a PDF</html>"),
            1 => Pdf()[..^8], 2 => Pdf("9.9"), 3 => Pdf()[..8],
            4 => Encoding.ASCII.GetBytes("prefix").Concat(Pdf()).ToArray(),
            _ => Pdf()
        };
        if(scenario==5)bytes[8]=(byte)'X';
        await Assert.ThrowsAsync<ArgumentException>(() => MediaValidation.CheckSignatureAsync(new MemoryStream(bytes),LessonContentType.Pdf,default));
    }
    [Theory]
    [InlineData(OrganizationRole.Instructor,true,false)]
    [InlineData(OrganizationRole.Admin,false,false)]
    [InlineData(OrganizationRole.Student,false,true)]
    public async Task Authorized_author_uploads_attaches_and_replaces_pdf(OrganizationRole role,bool owner,bool platform)
    {
        using var temp=new PrivateDirectory();
        var f=new LessonContentTests.Fixture(role,owner,lessonPublished:false,superAdmin:platform);
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,temp.Storage,f.Work.Object,f.Context);
        var bytes=Pdf();
        var asset=await handler.UploadAsync(f.Lesson.Id,"../../original.pdf","application/pdf",bytes.Length,new MemoryStream(bytes));
        Assert.Equal("application/pdf",asset.MediaType);Assert.Equal(LessonContentType.Pdf,asset.Type);
        Assert.Equal(asset.Id.ToString("N")+".media",Path.GetFileName(Assert.Single(Directory.GetFiles(temp.Root))));
        f.Assets.Setup(x=>x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        var item=await f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Pdf,"Slides",0,true,null,null,asset.Id));
        Assert.Equal(asset.Id,item.AssetId);
        var result=await handler.OpenAsync(f.Lesson.Id,item.Id);await result.Stream.DisposeAsync();
        await f.Handler.UpdateAsync(f.Lesson.Id,item.Id,new(LessonContentType.Text,"Text",0,true,"Body",null));
        Assert.Null(item.AssetId);Assert.Single(Directory.GetFiles(temp.Root));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.OpenAsync(f.Lesson.Id,item.Id));
        Assert.Equal("Legacy body",f.Lesson.Content);
    }
    [Theory]
    [InlineData(OrganizationRole.Instructor,false,false)]
    [InlineData(OrganizationRole.Student,true,false)]
    [InlineData(OrganizationRole.Admin,true,true)]
    public async Task Unauthorized_upload_and_attachment_do_not_write(OrganizationRole role,bool owner,bool foreign)
    {
        var f=new LessonContentTests.Fixture(role,owner,foreign);
        var storage=new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        Assert.NotNull(await Record.ExceptionAsync(()=>handler.UploadAsync(f.Lesson.Id,"slides.pdf","application/pdf",Pdf().Length,new MemoryStream(Pdf()))));
        Assert.NotNull(await Record.ExceptionAsync(()=>f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Pdf,"Slides",0,true,null,null,Guid.NewGuid()))));
        storage.VerifyNoOtherCalls();f.Assets.VerifyNoOtherCalls();f.Work.Verify(x=>x.SaveChangesAsync(default),Times.Never);
    }
    [Theory]
    [InlineData(true,true,0,true)] [InlineData(true,true,1,true)]
    [InlineData(false,true,0,false)] [InlineData(true,false,0,false)]
    [InlineData(true,true,2,false)] [InlineData(true,true,3,false)]
    public async Task Student_pdf_read_requires_publication_and_enrollment(bool lesson,bool course,int enrollment,bool allowed)
    {
        var f=new LessonContentTests.Fixture(OrganizationRole.Student,lessonPublished:lesson,coursePublished:course,enrollment:enrollment);
        var (item,asset)=Attach(f);
        var storage=new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        if(allowed)storage.Setup(x=>x.OpenReadAsync(asset.Id,default)).ReturnsAsync(new MemoryStream(Pdf()));
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        if(allowed) { var result=await handler.OpenAsync(f.Lesson.Id,item.Id);Assert.Equal("application/pdf",result.MediaType);await result.Stream.DisposeAsync(); }
        else
        {
            var error=await Record.ExceptionAsync(()=>handler.OpenAsync(f.Lesson.Id,item.Id));
            Assert.IsType(!lesson||!course ? typeof(KeyNotFoundException) : typeof(UnauthorizedAccessException),error);
            storage.VerifyNoOtherCalls();
        }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Pdf_assets_cannot_bypass_parent_organization_or_type(int scenario)
    {
        var f=new LessonContentTests.Fixture(foreign:scenario==3);
        var asset=new MediaAsset(Guid.NewGuid(),scenario==1?Guid.NewGuid():f.Lesson.Id,
            scenario==0?Guid.NewGuid():f.Context.OrganizationId!.Value,scenario==2?LessonContentType.Mp4:LessonContentType.Pdf,Pdf().Length);
        var item=new LessonContentItem(f.Lesson.Id,LessonContentType.Pdf,"Slides",0,true,null,null,asset.Id);f.Items.Add(item);
        f.Assets.Setup(x=>x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        var storage=new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.OpenAsync(f.Lesson.Id,item.Id));
        Assert.NotNull(await Record.ExceptionAsync(()=>f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Pdf,"Other",0,true,null,null,asset.Id))));
        storage.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Direct_asset_id_or_wrong_nested_lesson_cannot_retrieve_pdf()
    {
        var f=new LessonContentTests.Fixture();var (item,asset)=Attach(f);
        var storage=new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.OpenAsync(f.Lesson.Id,asset.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>handler.OpenAsync(Guid.NewGuid(),item.Id));
        storage.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Invalid_pdf_or_failed_save_cleans_files(bool saveFails)
    {
        using var temp=new PrivateDirectory();var f=new LessonContentTests.Fixture();
        if(saveFails)f.Work.Setup(x=>x.SaveChangesAsync(default)).ThrowsAsync(new InvalidOperationException("Save failed"));
        var handler=new MediaAssetHandlers(f.Handler,f.Assets.Object,temp.Storage,f.Work.Object,f.Context);
        var bytes=saveFails?Pdf():Encoding.ASCII.GetBytes("not a PDF at all");
        Assert.NotNull(await Record.ExceptionAsync(()=>handler.UploadAsync(f.Lesson.Id,"slides.pdf","application/pdf",bytes.Length,new MemoryStream(bytes))));
        Assert.Empty(Directory.GetFiles(temp.Root));
        if(!saveFails)f.Assets.Verify(x=>x.Add(It.IsAny<MediaAsset>()),Times.Never);
    }
    [Theory]
    [InlineData("GET","",200)] [InlineData("GET","bytes=0-7",206)]
    [InlineData("GET","bytes=999999-",416)] [InlineData("HEAD","",200)]
    public async Task Pdf_response_is_inline_private_and_supports_framework_ranges(string method,string range,int status)
    {
        var f=new LessonContentTests.Fixture(OrganizationRole.Student);var(item,asset)=Attach(f);
        var storage=new Mock<IPrivateMediaStorage>();storage.Setup(x=>x.OpenReadAsync(asset.Id,default)).ReturnsAsync(new MemoryStream(Pdf()));
        var http=new DefaultHttpContext();http.Request.Method=method;http.Request.Headers.Range=range;http.Response.Body=new MemoryStream();
        var controller=new LessonMediaController(new(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context))
            {ControllerContext=new ControllerContext {HttpContext=http}};
        var result=Assert.IsType<FileStreamResult>(await controller.Media(f.Lesson.Id,item.Id,default));
        Assert.Equal("application/pdf",result.ContentType);Assert.True(result.EnableRangeProcessing);
        await new FileStreamResultExecutor(NullLoggerFactory.Instance).ExecuteAsync(new ActionContext(http,new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),result);
        Assert.Equal(status,http.Response.StatusCode);
        Assert.Equal($"inline; filename=\"presentation-{item.Id:N}.pdf\"",http.Response.Headers.ContentDisposition);
        Assert.Equal("private, no-store",http.Response.Headers.CacheControl);Assert.Equal("nosniff",http.Response.Headers["X-Content-Type-Options"]);
        if(status==206) {Assert.Equal($"bytes 0-7/{Pdf().Length}",http.Response.Headers.ContentRange);Assert.Equal(8,http.Response.Body.Length);}
        if(status==416||method=="HEAD")Assert.Equal(0,http.Response.Body.Length);
        if(status==200&&method=="GET")Assert.Equal(Pdf(),((MemoryStream)http.Response.Body).ToArray());
    }
    private static (LessonContentItem Item,MediaAsset Asset) Attach(LessonContentTests.Fixture f)
    {
        var asset=new MediaAsset(Guid.NewGuid(),f.Lesson.Id,f.Context.OrganizationId!.Value,LessonContentType.Pdf,Pdf().Length);
        var item=new LessonContentItem(f.Lesson.Id,LessonContentType.Pdf,"Slides",0,true,null,null,asset.Id);f.Items.Add(item);
        f.Assets.Setup(x=>x.GetAsync(asset.Id,default)).ReturnsAsync(asset);return(item,asset);
    }
    private sealed class PrivateDirectory : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"lms-pdf-test-"+Guid.NewGuid().ToString("N"));
        public LocalPrivateMediaStorage Storage {get;}
        public PrivateDirectory()=>Storage=new(Root,Path.Combine(Root,"wwwroot"));
        public void Dispose()
        {
            if(!Directory.Exists(Root))return;
            foreach(var file in Directory.GetFiles(Root))File.Delete(file);
            Directory.Delete(Root);
        }
    }
}
