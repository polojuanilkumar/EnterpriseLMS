using LMS.Application.Interfaces.Lessons;
namespace LMS.Infrastructure.Storage;

public sealed class LocalPrivateMediaStorage : IPrivateMediaStorage
{
    private readonly string root;
    public LocalPrivateMediaStorage(string root, string webRoot)
    {
        this.root = Path.GetFullPath(root);
        var publicRoot = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((this.root + Path.DirectorySeparatorChar).StartsWith(publicRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Media storage must be outside wwwroot.");
    }
    private string Key(Guid id) => Path.Combine(root, id.ToString("N") + ".media");
    public async Task<IStagedMedia> StageAsync(Guid assetId, Stream source, long maxBytes, CancellationToken token)
    {
        if (assetId == Guid.Empty || maxBytes <= 0) throw new ArgumentException("Invalid storage request.");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".upload");
        var stream = new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,81920,FileOptions.Asynchronous);
        var lease = new Staged(stream,path,Key(assetId));
        try
        {
            byte[] buffer = new byte[81920]; long total = 0; int read;
            while ((read = await source.ReadAsync(buffer,token)) != 0)
            {
                total += read;
                if (total > maxBytes) throw new ArgumentException("Media exceeds its size limit.");
                await stream.WriteAsync(buffer.AsMemory(0,read),token);
            }
            if (total == 0) throw new ArgumentException("Media is empty.");
            await stream.FlushAsync(token); stream.Position = 0;
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    public Task<Stream> OpenReadAsync(Guid assetId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return Task.FromResult<Stream>(new FileStream(Key(assetId),FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous)); }
        catch (FileNotFoundException) { throw new KeyNotFoundException("Media file not found."); }
        catch (DirectoryNotFoundException) { throw new KeyNotFoundException("Media file not found."); }
    }
    private sealed class Staged(FileStream stream, string temporary, string destination) : IStagedMedia
    {
        private bool published, retained;
        public Stream Content => stream;
        public async Task PublishAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await stream.DisposeAsync();
            File.Move(temporary,destination); published = true;
        }
        public void Retain()
        {
            if (!published) throw new InvalidOperationException("Publish before retaining media.");
            retained = true;
        }
        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            if (!retained) File.Delete(published ? destination : temporary);
        }
    }
}
