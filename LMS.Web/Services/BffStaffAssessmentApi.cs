using LMS.Web.Models;
namespace LMS.Web.Services;
public sealed partial class LearnerApi
{
    public async Task<AssignmentPage> StaffAssignment(LearnerSession s,Guid course,Guid section,Guid lesson,Guid? assignment,Guid? submission,CancellationToken token)
    {
        var org=s.OrganizationId??throw new WebApiException(403);
        // The shared list client validates live actor/organization and the stored parent context.
        // Passing no assignment avoids the MVC client's unbounded submission-list request.
        var page=await AssignmentPage(s,org,course,section,lesson,true,null,null,token);
        if(assignment.HasValue)
        {
            var a=await Get<AssignmentView>(AssignmentPath(lesson,assignment),s,org,token);
            if(a.Id!=assignment||a.LessonId!=lesson)throw new WebApiException(404);
            page=page with {Assignment=a};
            if(submission.HasValue)
            {
                var work=await Get<AssignmentSubmissionView>(AssignmentPath(lesson,assignment)+$"/submissions/{submission}",s,org,token);
                if(work.Id!=submission||work.AssignmentId!=assignment)throw new WebApiException(404);
                page=page with {Submission=work};
            }
        }
        else if(submission.HasValue)throw new WebApiException(404);
        return page;
    }
    public async Task<StaffSubmissionPage> StaffAssignmentSubmissions(LearnerSession s,AssignmentPage page,int number,int size,CancellationToken token)
    {
        if(number is <1 or >10000||size is <1 or >50)throw new WebApiException(400);
        var a=page.Assignment??throw new WebApiException(404);
        var rows=await Get<StaffSubmissionPage>(AssignmentPath(a.LessonId,a.Id)+$"/submissions/page?page={number}&pageSize={size}",s,page.OrganizationId,token);
        if(rows.Page!=number||rows.PageSize!=size||rows.Total<0||rows.Items.Length>size||rows.Items.Any(x=>x.AssignmentId!=a.Id)||rows.Items.Select(x=>x.Id).Distinct().Count()!=rows.Items.Length)throw new WebApiException(502);
        return rows;
    }
    public async Task StaffSaveAssignment(LearnerSession s,AssignmentPage page,StaffAssignmentForm request,CancellationToken token)
    {
        var stored=page.Assignment;
        var change=stored is null||request.ProtectedFieldsChanged;
        var instructions=change?request.Instructions:stored!.Instructions;
        var marks=change?request.MaximumMarks:stored!.MaximumMarks;
        if(string.IsNullOrWhiteSpace(instructions)||!marks.HasValue)throw new AuthoringValidationException("Instructions and maximum marks are required.");
        // Preserve database ticks and line endings when a title-only edit leaves protected fields alone.
        // A changed protected field still reaches the API's serialized freeze checks.
        var due=stored is null||request.DueAtChanged?request.DueAtUtc:stored.DueAtUtc;
        await SaveAssignment(s,page,new AssignmentForm {Title=request.Title,Instructions=instructions,MaximumMarks=marks.Value,DueAtUtc=due?.UtcDateTime},token);
    }
    public async Task StaffDeleteQuiz(LearnerSession s,QuizEditorPage page,CancellationToken token)
        =>await AuthoringChange(HttpMethod.Delete,$"{page.Unit.Lesson.Id}/quiz",new {},s,page.Unit.OrganizationId,token);
}
