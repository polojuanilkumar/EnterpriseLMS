using LMS.Domain.Entities;
namespace LMS.Application.Interfaces.Lessons;
public interface IMediaAssetRepository
{
    Task<MediaAsset?> GetAsync(Guid id, CancellationToken token = default);
    void Add(MediaAsset asset);
}
