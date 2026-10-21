using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Lessons;
using LMS.Application.Interfaces.Persistence;
using LMS.Domain.Entities;
namespace LMS.Application.Features.LessonContent;

public sealed class MediaAssetHandlers(LessonContentHandlers content, IMediaAssetRepository assets,
    IPrivateMediaStorage storage, IUnitOfWork work, OrganizationContext organization)
{
    public async Task<MediaAsset> UploadAsync(Guid lessonId, string filename, string mediaType, long length,
        Stream source, CancellationToken token = default)
    {
        await content.AccessAsync(lessonId,true,token);
        var (type,limit) = MediaValidation.CheckDeclaration(filename,mediaType,length);
        var id = Guid.NewGuid();
        await using var staged = await storage.StageAsync(id,source,limit,token);
        if (staged.Content.Length != length) throw new ArgumentException("Declared and actual media lengths differ.");
        try { await MediaValidation.CheckSignatureAsync(staged.Content,type,token); }
        catch (EndOfStreamException) { throw new ArgumentException("Truncated media file."); }
        var asset = new MediaAsset(id,lessonId,organization.OrganizationId!.Value,type,length);
        await staged.PublishAsync(token);
        assets.Add(asset);
        await work.SaveChangesAsync(token);
        staged.Retain();
        return asset;
    }
    public async Task<(Stream Stream, string MediaType)> OpenAsync(Guid lessonId, Guid itemId, CancellationToken token = default)
    {
        var item = await content.GetAsync(lessonId,itemId,token);
        if (!item.AssetId.HasValue) throw new KeyNotFoundException("Media not found.");
        var asset = await assets.GetAsync(item.AssetId.Value,token);
        if (asset is null || asset.Id != item.AssetId || asset.LessonId != lessonId
            || asset.OrganizationId != organization.OrganizationId || asset.Type != item.Type)
            throw new KeyNotFoundException("Media not found.");
        return (await storage.OpenReadAsync(asset.Id,token), asset.MediaType);
    }
}
