using LMS.Domain.Enums;
using System.Text.RegularExpressions;

namespace LMS.Domain.Entities;

public sealed class LessonContentItem
{
    private LessonContentItem() { }
    public LessonContentItem(Guid lessonId, LessonContentType type, string title, int displayOrder,
        bool isRequired, string? text, string? youTubeUrl, Guid? assetId = null)
    {
        if (lessonId == Guid.Empty) throw new ArgumentException("Lesson is required.");
        Id = Guid.NewGuid(); LessonId = lessonId;
        Update(type, title, displayOrder, isRequired, text, youTubeUrl, assetId);
    }
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public LessonContentType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public bool IsRequired { get; private set; }
    public Guid? AssetId { get; private set; }
    public string? Text { get; private set; }
    public string? YouTubeVideoId { get; private set; }

    public void Update(LessonContentType type, string title, int displayOrder, bool isRequired,
        string? text, string? youTubeUrl, Guid? assetId = null)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentException("Unsupported content type.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Title must contain 1 to 200 characters.");
        if (displayOrder < 0) throw new ArgumentException("Display order cannot be negative.");
        string? videoId = null;
        if (type == LessonContentType.Text)
        {
            if (string.IsNullOrWhiteSpace(text) || youTubeUrl is not null)
                throw new ArgumentException("Text items require text and prohibit a YouTube URL.");
        }
        else if (type == LessonContentType.YouTube)
        {
            if (text is not null) throw new ArgumentException("YouTube items prohibit text.");
            videoId = NormalizeYouTubeUrl(youTubeUrl);
        }
        else if (text is not null || youTubeUrl is not null || assetId is null || assetId == Guid.Empty)
            throw new ArgumentException("Media items require an asset ID and prohibit text/YouTube URLs.");
        if (type is LessonContentType.Text or LessonContentType.YouTube && assetId is not null)
            throw new ArgumentException("Text/YouTube items prohibit asset IDs.");
        AssetId = assetId;
        Type = type; Title = title.Trim(); DisplayOrder = displayOrder; IsRequired = isRequired;
        Text = text; YouTubeVideoId = videoId;
    }
    public void Reorder(int order)
    {
        if (order < 0) throw new ArgumentException("Display order cannot be negative.");
        DisplayOrder = order;
    }

    public static string NormalizeYouTubeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace)
            || value.Any(char.IsControl) || value.Contains('\\') || value.Contains('%')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new ArgumentException("A supported HTTPS YouTube URL is required.");
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (host == "youtu.be" && path.Length == 1) id = path[0];
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if (uri.AbsolutePath == "/watch")
            {
                var values = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
                    .Where(x => x[0] == "v").ToArray();
                if (values.Length == 1 && values[0].Length == 2) id = values[0][1];
            }
            else if (path.Length == 2 && path[0] is "embed" or "shorts" or "live") id = path[1];
        }
        else if (host is "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            if (path.Length == 2 && path[0] == "embed") id = path[1];
        }
        if (id is null || !Regex.IsMatch(id, "\\A[A-Za-z0-9_-]{11}\\z"))
            throw new ArgumentException("Unsupported YouTube host, format, or video ID.");
        return id;
    }
}
