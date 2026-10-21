using System.Net.Http.Json;
using System.Text.Json;
using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    private static string AssignmentPath(Guid lesson,Guid? assignment=null)=>$"api/Lessons/{lesson}/assignments"+(assignment.HasValue?$"/{assignment}":"");
    public async Task<AssignmentPage> AssignmentPage(LearnerSession session,Guid org,Guid course,Guid section,Guid lesson,bool staff,Guid? assignment,Guid? submission,CancellationToken token)
    {
        if(staff)await StaffAccess(session,org,false,token);else await LearnerAccess(session,org,token);
        var context=await Get<AssignmentContextView>(AssignmentPath(lesson)+"/context",session,org,token);
        var p=context.Parent;
        if(p.OrganizationId!=org||p.CourseId!=course||p.SectionId!=section||p.LessonId!=lesson)throw new WebApiException(404);
        if(staff)
        {var access=await Access(session,org,token);if(!access.IsAdmin&&p.InstructorId!=session.UserId)throw new WebApiException(403);}
        else if(context.EnrollmentStatus is not (1 or 2))throw new WebApiException(403);
        var rows=await Get<AssignmentView[]>(AssignmentPath(lesson),session,org,token);
        if(rows.Any(x=>x.LessonId!=lesson)||rows.Select(x=>x.Id).Distinct().Count()!=rows.Length)throw new WebApiException(404);
        var page=new AssignmentPage(org,context,staff,rows);
        if(assignment.HasValue)
        {
            var a=await Get<AssignmentView>(AssignmentPath(lesson,assignment),session,org,token);
            if(a.Id!=assignment||a.LessonId!=lesson)throw new WebApiException(404);
            var work=await Get<AssignmentSubmissionView[]>(AssignmentPath(lesson,assignment)+(staff?"/submissions":"/submissions/me"),session,org,token);
            if(work.Any(x=>x.AssignmentId!=assignment||!staff&&x.UserId!=session.UserId))throw new WebApiException(403);
            page=page with {Assignment=a,Submissions=work,Form=new(){Title=a.Title,Instructions=a.Instructions,DueAtUtc=a.DueAtUtc?.UtcDateTime,MaximumMarks=a.MaximumMarks}};
            if(submission.HasValue)
            {
                var s=await Get<AssignmentSubmissionView>(AssignmentPath(lesson,assignment)+$"/submissions/{submission}",session,org,token);
                if(s.Id!=submission||s.AssignmentId!=assignment||!staff&&s.UserId!=session.UserId)throw new WebApiException(403);
                page=page with {Submission=s};
            }
        }
        return page;
    }
    public async Task SaveAssignment(LearnerSession session,AssignmentPage page,AssignmentForm form,CancellationToken token)
    {
        if(!page.Staff)throw new WebApiException(403);
        await AuthoringChange(page.Assignment is null?HttpMethod.Post:HttpMethod.Put,AssignmentPath(page.Context.Parent.LessonId,page.Assignment?.Id),new {form.Title,form.Instructions,DueAtUtc=form.DueAtUtc.HasValue?new DateTimeOffset(DateTime.SpecifyKind(form.DueAtUtc.Value,DateTimeKind.Utc)):(DateTimeOffset?)null,form.MaximumMarks},session,page.OrganizationId,token);
    }
    public async Task PublishAssignment(LearnerSession session,AssignmentPage page,bool published,bool confirmed,CancellationToken token)
    {
        if(!page.Staff||page.Assignment is null)throw new WebApiException(403);
        if(!confirmed)throw new AuthoringValidationException("Explicit publication confirmation is required.");
        await AuthoringChange(HttpMethod.Patch,AssignmentPath(page.Context.Parent.LessonId,page.Assignment.Id)+"/publication",new {IsPublished=published,Confirmed=true},session,page.OrganizationId,token);
    }
    public async Task SubmitAssignment(LearnerSession session,AssignmentPage page,AssignmentSubmissionForm form,CancellationToken token)
    {
        if(page.Staff||page.Assignment is null)throw new WebApiException(403);
        if(!form.Confirmed)throw new AuthoringValidationException("Confirm the final submission. There is no resubmission in v1.");
        if(!page.CanSubmit)throw new AuthoringValidationException("New submissions require Active enrollment and published content, with no existing submission.");
        if(string.IsNullOrWhiteSpace(form.Text)&&form.File is null)throw new AuthoringValidationException("Provide text, one PDF, or both.");
        if(form.File is not null&&(form.File.Length<=0||form.File.Length>26214400||!form.File.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)||!string.Equals(form.File.ContentType,"application/pdf",StringComparison.OrdinalIgnoreCase)))throw new AuthoringValidationException("Choose one PDF, up to 25 MiB. The server also verifies its signature.");
        if(form.File is not null&&(form.File.FileName.Any(char.IsControl)||Path.GetFileName(form.File.FileName.Replace('\\','/')).Length>200))throw new AuthoringValidationException("Attachment filename is invalid or exceeds 200 characters.");
        using var request=Request(HttpMethod.Post,AssignmentPath(page.Context.Parent.LessonId,page.Assignment.Id)+"/submissions",session,page.OrganizationId);
        using var body=new MultipartFormDataContent();body.Add(new StringContent(form.Text??""),"Text");body.Add(new StringContent("true"),"Confirmed");
        if(form.File is not null)
        {var part=new StreamContent(form.File.OpenReadStream());part.Headers.ContentType=new("application/pdf");body.Add(part,"File",Path.GetFileName(form.File.FileName.Replace('\\','/')));}
        request.Content=body;
        using var response=await http.SendAsync(request,token);
        await AssignmentOutcome(response,token);
    }
    private static async Task AssignmentOutcome(HttpResponseMessage response,CancellationToken token)
    {
        if((int)response.StatusCode is 400 or 409 or 413)
        {
            var message="Check the entered values. No automatic retry was made.";
            try {var body=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);if(body.TryGetProperty("message",out var value)&&value.ValueKind==JsonValueKind.String)message=value.GetString()??message;}catch(JsonException){}
            throw new AuthoringValidationException(message.Length>1000?message[..1000]:message,(int)response.StatusCode);
        }
        Ensure(response);
    }
    public async Task GradeAssignment(LearnerSession session,AssignmentPage page,AssignmentGradeForm form,CancellationToken token)
    {
        if(!page.Staff||page.Submission is null||page.Assignment is null)throw new WebApiException(403);
        if(!form.Confirmed)throw new AuthoringValidationException("Confirm the final grade. It cannot be overwritten.");
        await AuthoringChange(HttpMethod.Post,AssignmentPath(page.Context.Parent.LessonId,page.Assignment.Id)+$"/submissions/{page.Submission.Id}/grade",new {form.Marks,form.Feedback,Confirmed=true},session,page.OrganizationId,token);
    }
    public async Task<HttpResponseMessage> AssignmentDownload(LearnerSession session,AssignmentPage page,CancellationToken token,bool head=false)
    {
        if(page.Submission is not {HasAttachment:true}||page.Assignment is null)throw new WebApiException(404);
        using var request=Request(head?HttpMethod.Head:HttpMethod.Get,AssignmentPath(page.Context.Parent.LessonId,page.Assignment.Id)+$"/submissions/{page.Submission.Id}/attachment",session,page.OrganizationId);
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        try {Ensure(response);if(response.Content.Headers.ContentType?.MediaType!="application/pdf")throw new WebApiException(502);return response;}catch{response.Dispose();throw;}
    }
    public async Task<AssignmentHistoryPage> AssignmentHistory(LearnerSession session,Guid org,CancellationToken token)
    {await LearnerAccess(session,org,token);return new(org,await Get<AssignmentHistoryView[]>("api/Assignments/submissions/me",session,org,token));}
}
