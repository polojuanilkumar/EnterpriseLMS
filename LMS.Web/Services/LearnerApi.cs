using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed class WebApiException(int status, string? safeMessage = null) : Exception("The learning service request could not be completed.")
{
    public int Status { get; } = status is 400 or 401 or 403 or 404 or 409 or 413 or 429 ? status : 502;
    public string? SafeMessage { get; } = safeMessage;
}
public sealed class ApiLogin
{
    public string AccessToken { get; init; } = "";
    public Guid UserId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
public sealed partial class LearnerApi(HttpClient http)
{
    private sealed record Envelope<T>(bool Success,T? Data);
    private static void Ensure(HttpResponseMessage response)
    { if(!response.IsSuccessStatusCode)throw new WebApiException((int)response.StatusCode); }
    private static async Task<T> Data<T>(HttpResponseMessage response,CancellationToken token)
    {
        Ensure(response);
        var body=await response.Content.ReadFromJsonAsync<Envelope<T>>(cancellationToken:token);
        return body is {Success:true,Data:not null} ? body.Data : throw new WebApiException(502);
    }
    public async Task<ApiLogin> LoginAsync(LoginForm form,CancellationToken token)
    {
        using var response=await http.PostAsJsonAsync("api/Auth/login",form,token);
        return await Data<ApiLogin>(response,token);
    }
    private static HttpRequestMessage Request(HttpMethod method,string path,LearnerSession session,Guid? organization)
    {
        var request=new HttpRequestMessage(method,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",session.AccessToken);
        if(organization.HasValue)request.Headers.Add("X-Organization-Id",organization.Value.ToString());
        return request;
    }
    private async Task<T> Get<T>(string path,LearnerSession session,Guid? organization,CancellationToken token)
    {
        using var request=Request(HttpMethod.Get,path,session,organization);
        using var response=await http.SendAsync(request,token);
        return await Data<T>(response,token);
    }
    public Task<OrganizationView[]> Organizations(LearnerSession session,CancellationToken token)
        => Get<OrganizationView[]>("api/organizations/me",session,null,token);
    public async Task<OrganizationAccess> ValidateSelection(LearnerSession session,Guid organization,CancellationToken token)
    {
        var allowed=await Organizations(session,token);
        if(!allowed.Any(x=>x.Id==organization && x.IsActive))throw new WebApiException(403);
        return await Access(session,organization,token);
    }
    public async Task<OrganizationAccess> Access(LearnerSession session,Guid organization,CancellationToken token)
    {
        var access=await Get<OrganizationAccess>($"api/organizations/{organization}/context",session,organization,token);
        if(access.OrganizationId!=organization || access.UserId!=session.UserId
            || (!access.IsSuperAdmin && access.Role is not ("Student" or "Instructor" or "Admin")))throw new WebApiException(403);
        return access;
    }
    public async Task<CourseView[]> Courses(LearnerSession session,Guid organization,CancellationToken token)
    {
        await Get<JsonElement>("api/Enrollments/my-courses/progress",session,organization,token);
        var enrollments=await Get<EnrollmentView[]>("api/Enrollments/me",session,organization,token);
        var courses=await Get<CourseView[]>("api/Courses",session,organization,token);
        return courses.Where(c=>c.IsPublished && enrollments.Any(e=>e.CourseId==c.Id && e.Status is 1 or 2)).ToArray();
    }
    public async Task<SectionsPage> Sections(LearnerSession session,Guid organization,Guid courseId,CancellationToken token)
    {
        // Student-only API gate enforces publication and Active/Completed enrollment even for a guessed course ID.
        await Get<JsonElement>($"api/Courses/{courseId}/progress",session,organization,token);
        var course=await Get<CourseView>($"api/Courses/{courseId}",session,organization,token);
        if(course.Id!=courseId || !course.IsPublished)throw new WebApiException(404);
        var sections=await Get<SectionView[]>($"api/Courses/{courseId}/sections",session,organization,token);
        if(sections.Any(x=>x.CourseId!=courseId))throw new WebApiException(404);
        return new(organization,course,sections.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray());
    }
    public async Task<LessonsPage> Lessons(LearnerSession session,Guid organization,Guid courseId,Guid sectionId,CancellationToken token)
    {
        var page=await Sections(session,organization,courseId,token);
        var section=page.Sections.SingleOrDefault(x=>x.Id==sectionId) ?? throw new WebApiException(404);
        var lessons=await Get<LessonView[]>($"api/Sections/{sectionId}/lessons",session,organization,token);
        if(lessons.Any(x=>x.SectionId!=sectionId))throw new WebApiException(404);
        return new(organization,page.Course,section,lessons.Where(x=>x.IsPublished).OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray());
    }
    public async Task<UnitPage> Unit(LearnerSession session,Guid organization,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {
        var page=await Lessons(session,organization,courseId,sectionId,token);
        var lesson=page.Lessons.SingleOrDefault(x=>x.Id==lessonId) ?? throw new WebApiException(404);
        var contents=await Get<ContentView>($"api/Lessons/{lessonId}/content-items",session,organization,token);
        if(contents.Items.Any(x=>x.LessonId!=lessonId))throw new WebApiException(404);
        // Legacy and items are mutually exclusive even if a malformed response includes both.
        contents=new(contents.Items.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray(),contents.Items.Length==0?contents.Legacy:null);
        return new(organization,courseId,sectionId,lesson,contents);
    }
    public async Task<HttpResponseMessage> Media(LearnerSession session,UnitPage unit,Guid itemId,bool head,string? range,string? ifRange,CancellationToken token)
    {
        var item=unit.Content.Items.SingleOrDefault(x=>x.Id==itemId && x.Type is 3 or 4 or 5) ?? throw new WebApiException(404);
        using var request=Request(head?HttpMethod.Head:HttpMethod.Get,$"api/Lessons/{unit.Lesson.Id}/content-items/{itemId}/media",session,unit.OrganizationId);
        if(!string.IsNullOrEmpty(range))
        {
            if(!RangeHeaderValue.TryParse(range,out var parsed) || parsed.Unit!="bytes")throw new WebApiException(404);
            request.Headers.Range=parsed;
        }
        if(!string.IsNullOrEmpty(ifRange) && RangeConditionHeaderValue.TryParse(ifRange,out var condition))request.Headers.IfRange=condition;
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        try
        {
            if((int)response.StatusCode!=416)Ensure(response);
            var expected=item.Type switch {3=>"audio/mpeg",4=>"video/mp4",_=>"application/pdf"};
            if(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType!=expected)throw new WebApiException(502);
            return response;
        }
        catch {response.Dispose();throw;}
    }
}
