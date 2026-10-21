using LMS.Domain.Enums;
namespace LMS.Domain.Entities;

public sealed class MediaAsset
{
    private MediaAsset() { }
    public MediaAsset(Guid id, Guid lessonId, Guid organizationId, LessonContentType type, long size)
    {
        if (id == Guid.Empty || lessonId == Guid.Empty || organizationId == Guid.Empty || size <= 0
            || type is not (LessonContentType.Mp3 or LessonContentType.Mp4 or LessonContentType.Pdf)) throw new ArgumentException("Invalid media asset.");
        Id = id; LessonId = lessonId; OrganizationId = organizationId; Type = type; Size = size; CreatedAt = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public LessonContentType Type { get; private set; }
    public long Size { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string MediaType => Type switch
    {
        LessonContentType.Mp3 => "audio/mpeg",
        LessonContentType.Mp4 => "video/mp4",
        LessonContentType.Pdf => "application/pdf",
        _ => throw new InvalidOperationException("Unsupported asset type.")
    };
}
