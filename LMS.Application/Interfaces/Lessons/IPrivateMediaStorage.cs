namespace LMS.Application.Interfaces.Lessons;

// Implementations own generated keys; client filenames are never storage keys.
public interface IPrivateMediaStorage
{
    Task<IStagedMedia> StageAsync(Guid assetId, Stream source, long maxBytes, CancellationToken token);
    Task<Stream> OpenReadAsync(Guid assetId, CancellationToken token);
}
public interface IStagedMedia : IAsyncDisposable
{
    Stream Content { get; }
    Task PublishAsync(CancellationToken token);
    // Only called after metadata persistence succeeds. Dispose removes everything otherwise.
    void Retain();
}
