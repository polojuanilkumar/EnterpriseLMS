using System.Buffers.Binary;
using System.Text;
using LMS.Domain.Enums;
namespace LMS.Application.Features.LessonContent;

public static class MediaValidation
{
    public const long PdfLimit = 25 * 1024 * 1024;
    public const long Mp3Limit = 25 * 1024 * 1024;
    public const long Mp4Limit = 200 * 1024 * 1024;
    public static (LessonContentType Type, long Limit) CheckDeclaration(string filename, string mediaType, long length)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        var result = (extension, mediaType.ToLowerInvariant()) switch
        {
            (".mp3", "audio/mpeg") => (LessonContentType.Mp3, Mp3Limit),
            (".mp4", "video/mp4") => (LessonContentType.Mp4, Mp4Limit),
            (".pdf", "application/pdf") => (LessonContentType.Pdf, PdfLimit),
            _ => throw new ArgumentException("Only .mp3/audio/mpeg, .mp4/video/mp4 and .pdf/application/pdf are supported.")
        };
        if (length <= 0 || length > result.Item2) throw new ArgumentException("Media is empty or exceeds its size limit.");
        return result;
    }
    public static async Task CheckSignatureAsync(Stream stream, LessonContentType type, CancellationToken token)
    {
        if (!stream.CanSeek) throw new ArgumentException("Validation requires a seekable stream.");
        stream.Position = 0;
        var header = new byte[12];
        if (await stream.ReadAtLeastAsync(header, 12, false, token) < 12) throw new ArgumentException("Truncated media file.");
        if (type == LessonContentType.Mp3)
        {
            long offset = 0;
            if (Encoding.ASCII.GetString(header,0,3) == "ID3")
            {
                if (header[3] is < 2 or > 4 || header[4] == 255 || header.Skip(6).Take(4).Any(x => x >= 128))
                    throw new ArgumentException("Invalid ID3 header.");
                offset = 10L + (header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9]);
                if (header[3] == 4 && (header[5] & 16) != 0) offset += 10;
            }
            // Require two consecutive MPEG Layer III frames, not merely an ID3 prefix.
            for (var i = 0; i < 2; i++)
            {
                if (offset + 4 > stream.Length) throw new ArgumentException("Missing MP3 audio frames.");
                stream.Position = offset;
                await stream.ReadExactlyAsync(header.AsMemory(0,4), token);
                var version = (header[1] >> 3) & 3; var bitrate = header[2] >> 4; var rate = (header[2] >> 2) & 3;
                if (header[0] != 255 || (header[1] & 224) != 224 || version == 1 || (header[1] & 6) != 2
                    || bitrate is 0 or 15 || rate == 3) throw new ArgumentException("Invalid MP3 frame signature.");
                int[] rates = [44100,48000,32000];
                int[] high = [0,32,40,48,56,64,80,96,112,128,160,192,224,256,320];
                int[] low = [0,8,16,24,32,40,48,56,64,80,96,112,128,144,160];
                var sampleRate = rates[rate] / (version == 3 ? 1 : version == 2 ? 2 : 4);
                offset += (version == 3 ? 144000 * high[bitrate] : 72000 * low[bitrate]) / sampleRate + ((header[2] >> 1) & 1);
                if (offset > stream.Length) throw new ArgumentException("Truncated MP3 frame.");
            }
        }
        else if (type == LessonContentType.Mp4)
        {
            bool ftyp = false, moov = false, mdat = false;
            long offset = 0;
            while (offset < stream.Length)
            {
                if (stream.Length - offset < 8) throw new ArgumentException("Truncated MP4 box.");
                stream.Position = offset; await stream.ReadExactlyAsync(header.AsMemory(0,8),token);
                long size = BinaryPrimitives.ReadUInt32BigEndian(header); var kind = Encoding.ASCII.GetString(header,4,4); var prefix = 8;
                if (size == 1)
                {
                    await stream.ReadExactlyAsync(header.AsMemory(0,8),token);
                    var large = BinaryPrimitives.ReadUInt64BigEndian(header);
                    if (large > long.MaxValue) throw new ArgumentException("Invalid MP4 box length.");
                    size = (long)large; prefix = 16;
                }
                if (size == 0) size = stream.Length - offset;
                if (size < prefix || size > stream.Length - offset) throw new ArgumentException("Invalid MP4 box length.");
                if (kind == "ftyp")
                {
                    if (ftyp || size < prefix + 8 || size > 4096 || (size-prefix)%4 != 0) throw new ArgumentException("Invalid MP4 ftyp.");
                    var brands = new byte[(int)size-prefix]; await stream.ReadExactlyAsync(brands,token);
                    var supported = new[] { "isom", "iso2", "mp41", "mp42", "avc1" };
                    ftyp = supported.Contains(Encoding.ASCII.GetString(brands,0,4));
                    for (var j = 8; j < brands.Length; j += 4) ftyp |= supported.Contains(Encoding.ASCII.GetString(brands,j,4));
                    if (!ftyp) throw new ArgumentException("Unsupported MP4 brand.");
                }
                if (kind == "moov" && size > prefix) moov = true;
                if (kind == "mdat" && size > prefix) mdat = true;
                offset += size;
            }
            if (!ftyp || !moov || !mdat) throw new ArgumentException("MP4 requires ftyp, moov and mdat boxes.");
        }
        else if (type == LessonContentType.Pdf)
        {
            // Signature checks only: not a PDF parser, sanitizer or malware scanner.
            var version = Encoding.ASCII.GetString(header, 0, 8);
            if (!(version.StartsWith("%PDF-1.", StringComparison.Ordinal) && header[7] is >= (byte)'0' and <= (byte)'7')
                && version != "%PDF-2.0")
                throw new ArgumentException("Invalid PDF signature or unsupported version.");
            if (header[8] is not (10 or 13)) throw new ArgumentException("Invalid PDF header termination.");
            var tail = new byte[(int)Math.Min(1024, stream.Length)];
            stream.Position = stream.Length - tail.Length;
            await stream.ReadExactlyAsync(tail, token);
            var ending = Encoding.ASCII.GetString(tail).TrimEnd(' ', '\t', '\r', '\n', '\f', '\0');
            if (!ending.EndsWith("%%EOF", StringComparison.Ordinal))
                throw new ArgumentException("PDF is missing its final EOF marker.");
        }
        else throw new ArgumentException("Unsupported media type.");
        stream.Position = 0;
    }
}
