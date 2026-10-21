using System.Buffers.Binary;
using System.Text;
using LMS.Api.Controllers;
using LMS.Application.Features.LessonContent;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class MediaAssetTests
{
    internal static byte[] Mp3()
    {
        var data = new byte[834];
        foreach (var start in new[] { 0,417 }) { data[start]=255; data[start+1]=251; data[start+2]=144; }
        return data;
    }
    internal static byte[] Mp4()
    {
        using var stream = new MemoryStream();
        foreach (var (kind,payload) in new[] { ("ftyp",Encoding.ASCII.GetBytes("isom0000mp42")), ("moov",new byte[8]), ("mdat",new byte[20]) })
        {
            byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size,payload.Length+8);
            stream.Write(size); stream.Write(Encoding.ASCII.GetBytes(kind)); stream.Write(payload);
        }
        return stream.ToArray();
    }
    [Theory]
    [InlineData("a.mp3","audio/mpeg",1,true)]
    [InlineData("a.MP3","audio/mpeg",26214400,true)]
    [InlineData("a.mp4","video/mp4",209715200,true)]
    [InlineData("a.mp3","audio/mpeg",26214401,false)]
    [InlineData("a.mp4","video/mp4",209715201,false)]
    [InlineData("a.mp4","video/mp4",0,false)]
    [InlineData("a.mp3","video/mp4",100,false)]
    [InlineData("a.mp4","application/octet-stream",100,false)]
    [InlineData("a.exe","audio/mpeg",100,false)]
    [InlineData("a.mp3.exe","audio/mpeg",100,false)]
    public void Declarations_and_exact_limits(string filename,string mediaType,long length,bool accepted)
    {
        var error = Record.Exception(() => MediaValidation.CheckDeclaration(filename,mediaType,length));
        if (accepted) Assert.Null(error); else Assert.IsType<ArgumentException>(error);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_signatures_accept_frames_and_container(bool video)
    {
        using var stream = new MemoryStream(video ? Mp4() : Mp3());
        await MediaValidation.CheckSignatureAsync(stream,video ? LessonContentType.Mp4 : LessonContentType.Mp3,default);
        Assert.Equal(0,stream.Position);
    }
    [Fact]
    public async Task Mp3_with_id3_requires_audio_after_tag()
    {
        var data = new byte[10+Mp3().Length]; Encoding.ASCII.GetBytes("ID3").CopyTo(data,0);data[3]=4;Mp3().CopyTo(data,10);
        await MediaValidation.CheckSignatureAsync(new MemoryStream(data),LessonContentType.Mp3,default);
        await Assert.ThrowsAsync<ArgumentException>(() => MediaValidation.CheckSignatureAsync(new MemoryStream(data[..20]),LessonContentType.Mp3,default));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Spoofs_wrong_types_truncation_and_corrupt_boxes_rejected(int scenario)
    {
        var data = scenario switch { 0 => new byte[100], 1 => Mp4(), 2 => Mp3()[..500], 3 => Mp4()[..20], 4 => Mp3(), _ => Mp4() };
        var type = scenario < 3 ? LessonContentType.Mp3 : LessonContentType.Mp4;
        if (scenario == 5) data[0]=127;
        await Assert.ThrowsAsync<ArgumentException>(() => MediaValidation.CheckSignatureAsync(new MemoryStream(data),type,default));
    }
    [Fact]
    public async Task Successful_upload_uses_generated_id_and_private_metadata_then_attaches()
    {
        using var temp = new StorageFixture(); var f = new LessonContentTests.Fixture();
        MediaAsset? saved = null;
        f.Assets.Setup(x => x.Add(It.IsAny<MediaAsset>())).Callback((MediaAsset a) => saved = a);
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,temp.Storage,f.Work.Object,f.Context);
        using var source = new MemoryStream(Mp3());
        var asset = await handler.UploadAsync(f.Lesson.Id,"../../client-name.mp3","audio/mpeg",source.Length,source);
        Assert.Same(asset,saved); Assert.Equal(f.Context.OrganizationId,asset.OrganizationId);
        Assert.Equal(asset.Id.ToString("N")+".media",Path.GetFileName(Assert.Single(Directory.GetFiles(temp.Root))));
        f.Assets.Setup(x => x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        var item = await f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Mp3,"Audio",0,true,null,null,asset.Id));
        var opened = await handler.OpenAsync(f.Lesson.Id,item.Id);
        await using (opened.Stream) { Assert.True(opened.Stream.CanSeek); Assert.Equal(source.Length,opened.Stream.Length); }
        await f.Handler.DeleteAsync(f.Lesson.Id,item.Id);
        Assert.Single(Directory.GetFiles(temp.Root)); // committed asset retained, even after last item reference
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.OpenAsync(f.Lesson.Id,item.Id));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Failed_validation_or_metadata_save_removes_upload(int scenario)
    {
        using var temp = new StorageFixture(); var f = new LessonContentTests.Fixture();
        if (scenario == 2) f.Work.Setup(x => x.SaveChangesAsync(default)).ThrowsAsync(new InvalidOperationException("save failed"));
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,temp.Storage,f.Work.Object,f.Context);
        var data = scenario == 0 ? new byte[100] : Mp3();
        var error = await Record.ExceptionAsync(() => handler.UploadAsync(f.Lesson.Id,"a.mp3","audio/mpeg",data.Length+(scenario==1 ? 1 : 0),new MemoryStream(data)));
        Assert.NotNull(error); Assert.Empty(Directory.GetFiles(temp.Root));
        if (scenario != 2) f.Work.Verify(x => x.SaveChangesAsync(default),Times.Never);
    }
    [Fact]
    public async Task Stream_failure_size_overrun_and_cancellation_clean_staging()
    {
        using var temp = new StorageFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => temp.Storage.StageAsync(Guid.NewGuid(),new MemoryStream(new byte[11]),10,default));
        await Assert.ThrowsAsync<IOException>(() => temp.Storage.StageAsync(Guid.NewGuid(),new BrokenStream(),100,default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => temp.Storage.StageAsync(Guid.NewGuid(),new MemoryStream(Mp3()),1000,new CancellationToken(true)));
        Assert.Empty(Directory.GetFiles(temp.Root));
    }
    [Theory]
    [InlineData(OrganizationRole.Student,true,false)]
    [InlineData(OrganizationRole.Instructor,false,false)]
    [InlineData(OrganizationRole.Admin,true,true)]
    public async Task Upload_denial_never_touches_storage(OrganizationRole role,bool owner,bool foreign)
    {
        var f = new LessonContentTests.Fixture(role,owner,foreign); var storage = new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        Assert.NotNull(await Record.ExceptionAsync(() => handler.UploadAsync(f.Lesson.Id,"a.mp3","audio/mpeg",834,new MemoryStream(Mp3()))));
        storage.VerifyNoOtherCalls(); f.Assets.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData(true,true,0,true)] [InlineData(true,true,1,true)]
    [InlineData(false,true,0,false)] [InlineData(true,false,0,false)]
    [InlineData(true,true,2,false)] [InlineData(true,true,3,false)]
    public async Task Private_retrieval_enforces_publication_and_enrollment(bool lesson,bool course,int enrollment,bool allowed)
    {
        var f = new LessonContentTests.Fixture(OrganizationRole.Student,lessonPublished:lesson,coursePublished:course,enrollment:enrollment);
        var asset = new MediaAsset(Guid.NewGuid(),f.Lesson.Id,f.Context.OrganizationId!.Value,LessonContentType.Mp3,834);
        var item = new LessonContentItem(f.Lesson.Id,LessonContentType.Mp3,"Audio",0,true,null,null,asset.Id); f.Items.Add(item);
        f.Assets.Setup(x => x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        var storage = new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        if (allowed) storage.Setup(x => x.OpenReadAsync(asset.Id,default)).ReturnsAsync(new MemoryStream(Mp3()));
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        if (allowed) { var result = await handler.OpenAsync(f.Lesson.Id,item.Id); await result.Stream.DisposeAsync(); }
        else { Assert.NotNull(await Record.ExceptionAsync(() => handler.OpenAsync(f.Lesson.Id,item.Id))); storage.VerifyNoOtherCalls(); }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Foreign_asset_organization_lesson_or_type_cannot_attach_or_stream(int mismatch)
    {
        var f = new LessonContentTests.Fixture();
        var asset = new MediaAsset(Guid.NewGuid(),mismatch==1 ? Guid.NewGuid() : f.Lesson.Id,
            mismatch==0 ? Guid.NewGuid() : f.Context.OrganizationId!.Value,mismatch==2 ? LessonContentType.Mp4 : LessonContentType.Mp3,834);
        f.Assets.Setup(x => x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Mp3,"Audio",0,true,null,null,asset.Id)));
        var item = new LessonContentItem(f.Lesson.Id,LessonContentType.Mp3,"Audio",0,true,null,null,asset.Id);f.Items.Add(item);
        var storage = new Mock<IPrivateMediaStorage>(MockBehavior.Strict);
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.OpenAsync(f.Lesson.Id,item.Id)); storage.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Retained_assets_cannot_be_deleted_or_changed_even_without_item_reference()
    {
        var f = new LessonContentTests.Fixture();
        using var db = new LMSDbContext(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer("Server=unused;Database=unused").Options,f.Context);
        var asset = new MediaAsset(Guid.NewGuid(),f.Lesson.Id,f.Context.OrganizationId!.Value,LessonContentType.Mp3,834);
        db.Attach(asset); db.Remove(asset);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(asset).State=EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Theory]
    [InlineData("GET", "bytes=10-19", 206, 10)]
    [InlineData("GET", "bytes=900-999", 416, 0)]
    [InlineData("HEAD", "", 200, 0)]
    public async Task Controller_stream_is_private_and_framework_executes_ranges(string method,string range,int status,int bodyLength)
    {
        var f = new LessonContentTests.Fixture();
        var asset = new MediaAsset(Guid.NewGuid(),f.Lesson.Id,f.Context.OrganizationId!.Value,LessonContentType.Mp3,834);
        var item = new LessonContentItem(f.Lesson.Id,LessonContentType.Mp3,"Audio",0,true,null,null,asset.Id);f.Items.Add(item);
        f.Assets.Setup(x => x.GetAsync(asset.Id,default)).ReturnsAsync(asset);
        var storage = new Mock<IPrivateMediaStorage>();storage.Setup(x => x.OpenReadAsync(asset.Id,default)).ReturnsAsync(new MemoryStream(Mp3()));
        var http = new DefaultHttpContext();http.Request.Method=method;http.Request.Headers.Range=range;http.Response.Body=new MemoryStream();
        var controller = new LessonMediaController(new(f.Handler,f.Assets.Object,storage.Object,f.Work.Object,f.Context))
            { ControllerContext = new ControllerContext { HttpContext=http } };
        var result = Assert.IsType<FileStreamResult>(await controller.Media(f.Lesson.Id,item.Id,default));
        Assert.True(result.EnableRangeProcessing);Assert.Equal("private, no-store",http.Response.Headers.CacheControl);
        Assert.Equal("nosniff",http.Response.Headers["X-Content-Type-Options"]);
        await new FileStreamResultExecutor(NullLoggerFactory.Instance).ExecuteAsync(new ActionContext(http,new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),result);
        Assert.Equal(status,http.Response.StatusCode);
        if (status == 206) Assert.Equal("bytes 10-19/834",http.Response.Headers.ContentRange);
        if (status == 416) Assert.Equal("bytes */834",http.Response.Headers.ContentRange);
        Assert.Equal(bodyLength,http.Response.Body.Length);
    }
    [Fact]
    public void Storage_rejects_public_root_and_api_has_no_delete_asset_route()
    {
        var root=Path.Combine(Path.GetTempPath(),"wwwroot");
        Assert.Throws<ArgumentException>(() => new LocalPrivateMediaStorage(Path.Combine(root,"media"),root));
        Assert.DoesNotContain(typeof(LessonMediaController).GetMethods(),m => Attribute.IsDefined(m,typeof(HttpDeleteAttribute)));
    }
    [Fact]
    public async Task Mp4_upload_replacement_preserves_original_asset()
    {
        using var temp = new StorageFixture(); var f = new LessonContentTests.Fixture();
        var handler = new MediaAssetHandlers(f.Handler,f.Assets.Object,temp.Storage,f.Work.Object,f.Context);
        var video = await handler.UploadAsync(f.Lesson.Id,"clip.mp4","video/mp4",Mp4().Length,new MemoryStream(Mp4()));
        f.Assets.Setup(x => x.GetAsync(video.Id,default)).ReturnsAsync(video);
        var item = await f.Handler.CreateAsync(f.Lesson.Id,new(LessonContentType.Mp4,"Video",0,true,null,null,video.Id));
        await f.Handler.UpdateAsync(f.Lesson.Id,item.Id,new(LessonContentType.Text,"Reading",0,true,"Body",null));
        Assert.Null(item.AssetId);Assert.Single(Directory.GetFiles(temp.Root));
        Assert.Equal("Legacy body",f.Lesson.Content);
    }
    [Fact]
    public async Task Failed_publish_does_not_delete_existing_asset()
    {
        using var temp = new StorageFixture(); var id=Guid.NewGuid();
        await using (var first = await temp.Storage.StageAsync(id,new MemoryStream(Mp3()),1000,default))
        { await first.PublishAsync(default); first.Retain(); }
        await using (var second = await temp.Storage.StageAsync(id,new MemoryStream(Mp3()),1000,default))
            await Assert.ThrowsAsync<IOException>(() => second.PublishAsync(default));
        Assert.Single(Directory.GetFiles(temp.Root));
        await using var retained = await temp.Storage.OpenReadAsync(id,default);
        Assert.Equal(834,retained.Length);
    }
    [Theory]
    [InlineData(LessonContentType.Mp3)] [InlineData(LessonContentType.Mp4)] [InlineData(LessonContentType.Pdf)]
    public void Media_requires_asset_and_excludes_other_payloads(LessonContentType type)
    {
        Assert.Throws<ArgumentException>(() => new LessonContentItem(Guid.NewGuid(),type,"Media",0,true,null,null));
        Assert.Throws<ArgumentException>(() => new LessonContentItem(Guid.NewGuid(),type,"Media",0,true,"body",null,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new LessonContentItem(Guid.NewGuid(),type,"Media",0,true,null,"https://youtu.be/dQw4w9WgXcQ",Guid.NewGuid()));
    }
    [Fact]
    public void Asset_model_is_private_scoped_and_restricts_cascade_deletion()
    {
        using var db = new LMSDbContext(new DbContextOptionsBuilder<LMSDbContext>().UseSqlServer("Server=unused;Database=unused").Options);
        var entity = db.Model.FindEntityType(typeof(MediaAsset))!;
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.All(entity.GetForeignKeys(),x => Assert.Equal(DeleteBehavior.Restrict,x.DeleteBehavior));
        Assert.Contains("OrganizationId",db.MediaAssets.ToQueryString());
        Assert.Contains("CourseSections",db.MediaAssets.ToQueryString());
        Assert.DoesNotContain(entity.GetProperties(),x => x.Name.Contains("Path") || x.Name.Contains("FileName"));
    }
    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default) => ValueTask.FromException<int>(new IOException("interrupted"));
    }
    private sealed class StorageFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"lms-media-test-"+Guid.NewGuid().ToString("N"));
        public LocalPrivateMediaStorage Storage { get; }
        public StorageFixture() => Storage = new(Root,Path.Combine(Root,"wwwroot"));
        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.GetFiles(Root)) File.Delete(file);
            Directory.Delete(Root);
        }
    }
}
