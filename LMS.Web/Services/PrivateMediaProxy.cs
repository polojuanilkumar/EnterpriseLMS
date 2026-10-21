namespace LMS.Web.Services;
public static class PrivateMediaProxy
{
    public static async Task WriteAsync(HttpContext http,HttpResponseMessage upstream,int type,Guid itemId,bool download,CancellationToken token)
    {
        http.Response.StatusCode=(int)upstream.StatusCode;
        foreach(var name in new[] {"Accept-Ranges","ETag","Last-Modified","Content-Range","Content-Length"})
        {
            if(upstream.Headers.TryGetValues(name,out var values) || upstream.Content.Headers.TryGetValues(name,out values))
                http.Response.Headers[name]=string.Join(", ",values);
        }
        http.Response.ContentType=type switch {3=>"audio/mpeg",4=>"video/mp4",5=>"application/pdf",_=>"application/octet-stream"};
        http.Response.Headers.CacheControl="private, no-store";
        http.Response.Headers["X-Content-Type-Options"]="nosniff";
        if(type is 3 or 4)
            http.Response.Headers.ContentDisposition=$"inline; filename=\"media-{itemId:N}.{(type==3 ? "mp3" : "mp4")}\"";
        if(type==5)
        {
            // Opaque-origin PDF document; never give uploaded PDF application script privileges.
            http.Response.Headers["Content-Security-Policy"]="sandbox; default-src 'none'; frame-ancestors 'self'";
            http.Response.Headers.ContentDisposition=$"{(download ? "attachment" : "inline")}; filename=\"presentation-{itemId:N}.pdf\"";
        }
        if(!HttpMethods.IsHead(http.Request.Method))await upstream.Content.CopyToAsync(http.Response.Body,token);
    }
}
