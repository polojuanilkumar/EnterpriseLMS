using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;

public sealed partial class LearnerApi
{
    public async Task<CourseEditor> CourseEditor(LearnerSession session,Guid org,Guid? course,CancellationToken token,OrganizationFilterForm? instructorFilter=null)
    {
        var access=await StaffAccess(session,org,false,token);
        var form=new CourseForm();
        if(course.HasValue)
        {
            await StaffSections(session,org,course.Value,token);
            var detail=await Get<CourseDetail>($"api/Courses/{course}",session,org,token);
            if(detail.Id!=course)throw new WebApiException(404);
            form=new(){Title=detail.Title,Code=detail.Code,Description=detail.Description,CategoryId=detail.CategoryId,
                InstructorId=detail.InstructorId,Level=detail.Level switch {"Intermediate"=>2,"Advanced"=>3,_=>1},
                DurationInMinutes=detail.DurationInMinutes,ThumbnailUrl=detail.ThumbnailUrl};
        }
        var categories=await Get<CategoryChoice[]>("api/Categories",session,org,token);
        instructorFilter ??=new OrganizationFilterForm {PageSize=50};instructorFilter.Role=2;
        var choices=access.IsAdmin&&!course.HasValue?await Members(session,org,instructorFilter,token):null;
        return new(org,course,form,categories,choices?.Members??[],access.IsAdmin,choices);
    }
    public async Task SaveCourse(LearnerSession session,Guid org,Guid? course,CourseForm form,CancellationToken token)
    {
        var access=await StaffAccess(session,org,false,token);
        if(course.HasValue)await StaffSections(session,org,course.Value,token);
        else if(access.IsAdmin)
        {
            if(!form.InstructorId.HasValue)throw new AuthoringValidationException("Select a current Instructor in this organization.");
            try
            {
                var member=await OrganizationMember(session,org,form.InstructorId.Value,token);
                if(member.Role!=2)throw new AuthoringValidationException("Select a current Instructor in this organization.");
            }
            catch(WebApiException ex) when(ex.Status==404){throw new AuthoringValidationException("Select a current Instructor in this organization.");}
        }
        else form.InstructorId=session.UserId;
        await AuthoringChange(course.HasValue?HttpMethod.Put:HttpMethod.Post,course.HasValue?$"api/Courses/{course}":"api/Courses",form,session,org,token);
    }
    public async Task<SectionEditor> SectionEditor(LearnerSession session,Guid org,Guid course,Guid? section,CancellationToken token)
    {
        var parent=await StaffSections(session,org,course,token);
        var form=new SectionForm {DisplayOrder=parent.Sections.Select(x=>x.DisplayOrder).DefaultIfEmpty(0).Max()+1};
        if(section.HasValue)
        {
            if(!parent.Sections.Any(x=>x.Id==section))throw new WebApiException(404);
            var detail=await Get<SectionDetail>($"api/Courses/{course}/sections/{section}",session,org,token);
            if(detail.Id!=section||detail.CourseId!=course)throw new WebApiException(404);
            form=new(){Title=detail.Title,Description=detail.Description,DisplayOrder=detail.DisplayOrder};
        }
        return new(org,course,section,form);
    }
    public async Task SaveSection(LearnerSession session,Guid org,Guid course,Guid? section,SectionForm form,CancellationToken token)
    {
        await SectionEditor(session,org,course,section,token);
        await AuthoringChange(section.HasValue?HttpMethod.Put:HttpMethod.Post,$"api/Courses/{course}/sections"+(section.HasValue?$"/{section}":""),form,session,org,token);
    }
    public async Task<LessonEditor> LessonEditor(LearnerSession session,Guid org,Guid course,Guid section,Guid? lesson,CancellationToken token)
    {
        var parent=await StaffLessons(session,org,course,section,token);
        var form=new LessonForm {DisplayOrder=parent.Lessons.Select(x=>x.DisplayOrder).DefaultIfEmpty(0).Max()+1};
        if(lesson.HasValue)
        {
            if(!parent.Lessons.Any(x=>x.Id==lesson))throw new WebApiException(404);
            var detail=await Get<LessonDetail>($"api/Lessons/{lesson}",session,org,token);
            if(detail.Id!=lesson||detail.SectionId!=section)throw new WebApiException(404);
            form=new(){Title=detail.Title,Description=detail.Description,DisplayOrder=detail.DisplayOrder,Content=detail.Content,
                VideoUrl=detail.VideoUrl,LessonType=detail.LessonType=="Video"?2:1,DurationInMinutes=detail.DurationInMinutes};
        }
        return new(org,course,section,lesson,form);
    }
    public async Task SaveLesson(LearnerSession session,Guid org,Guid course,Guid section,Guid? lesson,LessonForm form,CancellationToken token)
    {
        await LessonEditor(session,org,course,section,lesson,token);
        await AuthoringChange(lesson.HasValue?HttpMethod.Put:HttpMethod.Post,lesson.HasValue?$"api/Lessons/{lesson}":$"api/Sections/{section}/lessons",form,session,org,token);
    }
    private async Task AuthoringChange(HttpMethod method,string path,object form,LearnerSession session,Guid org,CancellationToken token)
    {
        using var request=Request(method,path,session,org);
        request.Content=JsonContent.Create(form);
        using var response=await http.SendAsync(request,token);
        // Only validation responses may contribute visible messages. Never surface auth/error diagnostics.
        if((int)response.StatusCode is 400 or 409)
        {
            var messages=new List<string>();
            try
            {
                var body=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
                if(body.TryGetProperty("errors",out var errors)&&errors.ValueKind==JsonValueKind.Object)
                    foreach(var property in errors.EnumerateObject())
                        if(property.Value.ValueKind==JsonValueKind.Array)
                            messages.AddRange(property.Value.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!));
                if(body.TryGetProperty("message",out var message)&&message.ValueKind==JsonValueKind.String)messages.Add(message.GetString()!);
            }
            catch(JsonException) { }
            throw new AuthoringValidationException(string.Join(" ",messages.Take(10).Select(x=>x.Length>500?x[..500]:x)) is {Length:>0} value?value:"Check the entered values and try again.", (int)response.StatusCode);
        }
        Ensure(response);
    }
    public async Task<ReadinessPage> Readiness(LearnerSession session,Guid org,Guid course,CancellationToken token)
    {
        var parent=await StaffSections(session,org,course,token);
        var detail=await Get<CourseDetail>($"api/Courses/{course}",session,org,token);
        if(detail.Id!=course)throw new WebApiException(404);
        var sections=new List<ReadySection>();
        foreach(var section in parent.Sections)
        {
            var lessons=await StaffLessons(session,org,course,section.Id,token);
            var units=new List<ReadyLesson>();
            foreach(var lesson in lessons.Lessons)
            {
                var unit=await StaffUnit(session,org,course,section.Id,lesson.Id,token);
                QuizState? quiz=null;
                try {quiz=await Get<QuizState>($"{lesson.Id}/quiz",session,org,token);}
                catch(WebApiException ex) when(ex.Status==404) { }
                if(quiz is not null&&quiz.LessonId!=lesson.Id)throw new WebApiException(404);
                units.Add(new(unit,quiz));
            }
            sections.Add(new(section,units.ToArray()));
        }
        return new(org,detail,sections.ToArray());
    }
}
public sealed class AuthoringValidationException(string message, int status = 400):Exception(message)
{ public int Status { get; } = status is 409 or 413 ? status : 400; }
