using System.Net.Http.Headers;
using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public async Task<AssetView> StaffUpload(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,int type,IFormFile file,CancellationToken token)
    {
        await StaffUnit(session,org,course,section,lesson,token);
        if(type is not (3 or 4 or 5) || file.Length<=0)throw new WebApiException(400);
        if(file.Length>(type==4?200L:25L)*1024*1024)throw new WebApiException(413);
        await using var stream=file.OpenReadStream();
        using var request=Request(HttpMethod.Post,$"api/Lessons/{lesson}/media-assets",session,org);
        using var multipart=new MultipartFormDataContent();
        var content=new StreamContent(stream);
        if(!MediaTypeHeaderValue.TryParse(file.ContentType,out var mime))throw new WebApiException(400);
        content.Headers.ContentType=mime;
        multipart.Add(content,"file",Path.GetFileName(file.FileName));request.Content=multipart;
        using var response=await http.SendAsync(request,token);
        var asset=await Data<AssetView>(response,token);
        if(asset.OrganizationId!=org || asset.LessonId!=lesson || asset.Type!=type)throw new WebApiException(400);
        return asset;
    }
}
