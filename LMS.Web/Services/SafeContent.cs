using System.Text.RegularExpressions;
namespace LMS.Web.Services;
public static class SafeContent
{
    public static string? YouTubeEmbed(string? id) => id is not null && Regex.IsMatch(id,"\\A[A-Za-z0-9_-]{11}\\z")
        ? "https://www.youtube-nocookie.com/embed/"+id : null;
    public static string? LegacyYouTube(string? url)
    {
        if(string.IsNullOrWhiteSpace(url) || url.Contains('\\') || url.Contains('%') || url.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || !uri.IsDefaultPort || uri.UserInfo.Length!=0)return null;
        var parts=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(uri.Host=="youtu.be" && parts.Length==1)return YouTubeEmbed(parts[0]);
        if(uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if(parts.Length==2 && parts[0] is "embed" or "shorts" or "live")return YouTubeEmbed(parts[1]);
            var values=uri.Query.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).Where(x=>x[0]=="v").ToArray();
            if(uri.AbsolutePath=="/watch" && values.Length==1 && values[0].Length==2)return YouTubeEmbed(values[0][1]);
        }
        if(uri.Host is "youtube-nocookie.com" or "www.youtube-nocookie.com" && parts.Length==2 && parts[0]=="embed")return YouTubeEmbed(parts[1]);
        return null;
    }
}
