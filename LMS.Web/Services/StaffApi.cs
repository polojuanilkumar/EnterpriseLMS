using System.Net.Http.Json;
using System.Net.Http.Headers;
using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    private async Task<OrganizationAccess> StaffAccess(LearnerSession session, Guid organization, bool admin, CancellationToken token)
    {
        if(session.OrganizationId!=organization)throw new WebApiException(403);
        var access=await Access(session,organization,token);
        if(!access.IsStaff || (admin && !access.IsAdmin))throw new WebApiException(403);
        return access;
    }
    public async Task<StaffCoursesPage> StaffCourses(LearnerSession session,Guid organization,CancellationToken token)
    {
        var access=await StaffAccess(session,organization,false,token);
        var courses=await Get<CourseView[]>("api/Courses",session,organization,token);
        return new(access,courses.Where(x=>access.IsAdmin || x.InstructorId==session.UserId).ToArray());
    }
    public async Task<SectionsPage> StaffSections(LearnerSession session,Guid organization,Guid courseId,CancellationToken token)
    {
        var access=await StaffAccess(session,organization,false,token);
        var course=await Get<CourseView>($"api/Courses/{courseId}",session,organization,token);
        if(course.Id!=courseId)throw new WebApiException(404);
        if(!access.IsAdmin && course.InstructorId!=session.UserId)throw new WebApiException(403);
        var sections=await Get<SectionView[]>($"api/Courses/{courseId}/sections",session,organization,token);
        if(sections.Any(x=>x.CourseId!=courseId))throw new WebApiException(404);
        return new(organization,course,sections.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray());
    }
    public async Task<LessonsPage> StaffLessons(LearnerSession session,Guid organization,Guid courseId,Guid sectionId,CancellationToken token)
    {
        var parent=await StaffSections(session,organization,courseId,token);
        var section=parent.Sections.SingleOrDefault(x=>x.Id==sectionId)??throw new WebApiException(404);
        var lessons=await Get<LessonView[]>($"api/Sections/{sectionId}/lessons",session,organization,token);
        if(lessons.Any(x=>x.SectionId!=sectionId))throw new WebApiException(404);
        return new(organization,parent.Course,section,lessons.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray());
    }
    public async Task<UnitPage> StaffUnit(LearnerSession session,Guid organization,Guid courseId,Guid sectionId,Guid lessonId,CancellationToken token)
    {
        var parent=await StaffLessons(session,organization,courseId,sectionId,token);
        var lesson=parent.Lessons.SingleOrDefault(x=>x.Id==lessonId)??throw new WebApiException(404);
        var content=await Get<ContentView>($"api/Lessons/{lessonId}/content-items",session,organization,token);
        if(content.Items.Any(x=>x.LessonId!=lessonId))throw new WebApiException(404);
        return new(organization,courseId,sectionId,lesson,new(content.Items.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).ToArray(),content.Items.Length==0?content.Legacy:null));
    }
    private async Task SendChange(HttpMethod method,string path,object? body,LearnerSession session,Guid org,CancellationToken token)
    {
        using var request=Request(method,path,session,org);
        if(body is not null)request.Content=JsonContent.Create(body);
        using var response=await http.SendAsync(request,token);
        Ensure(response); // Several existing mutations return success/message without data.
    }
    public async Task SaveItem(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,Guid? itemId,ContentForm form,CancellationToken token)
    {
        var unit=await StaffUnit(session,org,course,section,lesson,token);
        if(itemId.HasValue && !unit.Content.Items.Any(x=>x.Id==itemId))throw new WebApiException(404);
        if(form.File is not null)
        {
            if(form.Type is not (3 or 4 or 5) || form.AssetId.HasValue)throw new WebApiException(400);
            // API independently validates signature, size, MIME, parent and role. Do not trust client filenames as keys.
            await using var stream=form.File.OpenReadStream();
            using var upload=Request(HttpMethod.Post,$"api/Lessons/{lesson}/media-assets",session,org);
            using var multipart=new MultipartFormDataContent();
            var fileContent=new StreamContent(stream);
            if(!MediaTypeHeaderValue.TryParse(form.File.ContentType,out var mime))throw new WebApiException(400);
            fileContent.Headers.ContentType=mime;
            multipart.Add(fileContent,"file",Path.GetFileName(form.File.FileName));upload.Content=multipart;
            using var result=await http.SendAsync(upload,token);
            var asset=await Data<AssetView>(result,token);
            if(asset.OrganizationId!=org || asset.LessonId!=lesson || asset.Type!=form.Type)throw new WebApiException(400);
            form.AssetId=asset.Id;
        }
        var body=new {form.Type,form.Title,form.DisplayOrder,form.IsRequired,form.Text,form.YouTubeUrl,form.AssetId};
        await SendChange(itemId.HasValue?HttpMethod.Put:HttpMethod.Post,$"api/Lessons/{lesson}/content-items"+(itemId.HasValue?$"/{itemId}":""),body,session,org,token);
    }
    public async Task DeleteItem(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,Guid item,CancellationToken token)
    {
        var unit=await StaffUnit(session,org,course,section,lesson,token);
        if(!unit.Content.Items.Any(x=>x.Id==item))throw new WebApiException(404);
        await SendChange(HttpMethod.Delete,$"api/Lessons/{lesson}/content-items/{item}",null,session,org,token);
    }
    public async Task ReorderItems(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,Guid[] ids,CancellationToken token)
    {
        var unit=await StaffUnit(session,org,course,section,lesson,token);
        if(ids.Length!=unit.Content.Items.Length || ids.Distinct().Count()!=ids.Length || ids.Any(id=>!unit.Content.Items.Any(x=>x.Id==id)))throw new WebApiException(400);
        await SendChange(HttpMethod.Put,$"api/Lessons/{lesson}/content-items/reorder",new {itemIds=ids},session,org,token);
    }
    public async Task Publish(LearnerSession session,Guid org,Guid course,Guid? section,Guid? lesson,string action,CancellationToken token)
    {
        if(lesson.HasValue)
        {
            if(!section.HasValue || action is not ("publish" or "unpublish"))throw new WebApiException(400);
            await StaffUnit(session,org,course,section.Value,lesson.Value,token);
            await SendChange(HttpMethod.Patch,$"api/Lessons/{lesson}/{action}",null,session,org,token);
        }
        else
        {
            if(action is not ("publish" or "archive"))throw new WebApiException(400);
            await StaffSections(session,org,course,token);
            await SendChange(HttpMethod.Post,$"api/Courses/{course}/{action}",null,session,org,token);
        }
    }
    public async Task<MembersPage> Members(LearnerSession session,Guid org,CancellationToken token)
        => await Members(session,org,new OrganizationFilterForm(),token);
    public async Task SetMember(LearnerSession session,Guid org,MemberForm form,CancellationToken token)
    {
        await StaffAccess(session,org,true,token);
        if(!form.Confirmed || form.Role is <1 or >3)throw new WebApiException(400,"Confirm the membership action and select a valid role.");
        await AdministrationChange(form.UserId==Guid.Empty?HttpMethod.Post:HttpMethod.Put,
            $"api/organizations/{org}/members"+(form.UserId==Guid.Empty?"":$"/{form.UserId}"),
            new {form.Email,form.Role,form.Confirmed},session,org,token);
    }
    public async Task<InstructorPage> InstructorChoices(LearnerSession session,Guid org,Guid courseId,CancellationToken token,OrganizationFilterForm? filter=null)
    {
        filter ??=new OrganizationFilterForm {PageSize=50};filter.Role=2;
        var members=await Members(session,org,filter,token);
        var course=await StaffSections(session,org,courseId,token);
        return new(org,course.Course,members.Members,members);
    }
    public async Task AssignInstructor(LearnerSession session,Guid org,Guid course,Guid instructorId,CancellationToken token)
    {
        await StaffAccess(session,org,true,token);
        await StaffSections(session,org,course,token);
        if(instructorId==Guid.Empty)throw new WebApiException(400);
        // The API validates the stored Instructor membership under the shared write guard;
        // the bounded choice page is not an authorization check.
        await SendChange(HttpMethod.Put,$"api/Courses/{course}/instructor",new {instructorId},session,org,token);
    }
}
