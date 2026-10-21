using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;
public sealed class MediaAssetRepository(LMSDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> GetAsync(Guid id, CancellationToken token = default)
        => db.MediaAssets.SingleOrDefaultAsync(x => x.Id == id, token);
    public void Add(MediaAsset asset) => db.MediaAssets.Add(asset);
}
