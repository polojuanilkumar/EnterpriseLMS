using LMS.Application.Features.Organizations;
using LMS.Application.Features.LessonContent;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
namespace LMS.Application.Features.Assignments;

public sealed class AssignmentHandlers(IAssignmentRepository repo,IAssignmentStorage storage,OrganizationContext org,TimeProvider clock)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private static DateTimeOffset Utc(DateTime value)=>new(DateTime.SpecifyKind(value,DateTimeKind.Utc));
    public async Task<AssignmentParent> Access(Guid lesson,bool staff,bool mutation,CancellationToken token)
    {
        if(!org.OrganizationId.HasValue||org.UserId==Guid.Empty)throw new UnauthorizedAccessException("Select a verified organization.");
        var parent=await repo.Parent(lesson,token)??throw new KeyNotFoundException("Lesson not found.");
        if(parent.LessonId!=lesson||parent.OrganizationId!=org.OrganizationId)throw new KeyNotFoundException("Lesson not found.");
        if(staff)
        {
            if(!org.CanManageContent||(!org.IsSuperAdmin&&org.Role==OrganizationRole.Instructor&&parent.InstructorId!=org.UserId))throw new UnauthorizedAccessException("Assigned course ownership is required.");
        }
        else
        {
            if(org.IsSuperAdmin||org.Role!=OrganizationRole.Student)throw new UnauthorizedAccessException("Verified Student membership is required.");
            var state=await repo.Enrollment(parent.CourseId,org.UserId,token);
            if(state!=1&&!(state==2&&!mutation))throw new UnauthorizedAccessException("An Active enrollment is required to submit; Active or Completed is required to review.");
            if(mutation&&(!parent.CoursePublished||!parent.LessonPublished))throw new KeyNotFoundException("Published lesson not found.");
        }
        return parent;
    }
    private async Task<LessonAssignment> Stored(Guid lesson,Guid id,CancellationToken token)
    {var a=await repo.Get(id,token);return a is not null&&a.LessonId==lesson&&a.Id==id?a:throw new KeyNotFoundException("Assignment not found.");}
    public async Task<AssignmentContext> Context(Guid lesson,bool staff,CancellationToken token)
    {var p=await Access(lesson,staff,false,token);return new(p,staff?null:await repo.Enrollment(p.CourseId,org.UserId,token));}
    public async Task<AssignmentHistory[]> History(CancellationToken token)
    {
        if(!org.OrganizationId.HasValue||org.IsSuperAdmin||org.Role!=OrganizationRole.Student||org.UserId==Guid.Empty)throw new UnauthorizedAccessException("Select a verified Student organization.");
        var result=new List<AssignmentHistory>();
        foreach(var s in await repo.History(org.UserId,token))
        {
            if(s.UserId!=org.UserId)throw new UnauthorizedAccessException("Submission ownership mismatch.");
            var a=await repo.Get(s.AssignmentId,token)??throw new KeyNotFoundException("Assignment not found.");
            var p=await repo.Parent(a.LessonId,token)??throw new KeyNotFoundException("Lesson not found.");
            if(p.OrganizationId!=org.OrganizationId||p.LessonId!=a.LessonId)throw new KeyNotFoundException("Lesson not found.");
            if(await repo.Enrollment(p.CourseId,org.UserId,token) is not (1 or 2))continue;
            result.Add(new(p.LessonId,p.SectionId,p.CourseId,a.Id,s.Id,a.Title,Utc(s.SubmittedAt),await repo.Grade(s.Id,token) is not null));
        }
        return result.ToArray();
    }
    private async Task<AssignmentResponse> Map(LessonAssignment a,CancellationToken token)
        =>new(a.Id,a.LessonId,a.Title,a.Instructions,a.DueAtUtc is {} due?Utc(due):null,a.MaximumMarks,a.IsPublished,Utc(a.CreatedAt),a.UpdatedAt is {} updated?Utc(updated):null,await repo.HasSubmissions(a.Id,token));
    private async Task<SubmissionResponse> Map(AssignmentSubmission s,CancellationToken token)
    {var g=await repo.Grade(s.Id,token);return new(s.Id,s.AssignmentId,s.UserId,s.Text,s.AttachmentId.HasValue,s.Filename,Utc(s.SubmittedAt),s.InstructionsSnapshot,s.MaximumMarksSnapshot,g is null?null:new(g.Marks,g.Feedback,g.GraderId,Utc(g.GradedAt)));}
    private static void Validate(AssignmentRequest r)
    {
        if(string.IsNullOrWhiteSpace(r.Title)||r.Title.Length>200||string.IsNullOrWhiteSpace(r.Instructions)||r.Instructions.Length>20000)throw new ArgumentException("Title and plain-text instructions are required and must fit their limits.");
        if(r.MaximumMarks<=0||r.MaximumMarks>9999999999999999.99m||decimal.Round(r.MaximumMarks,2)!=r.MaximumMarks)throw new ArgumentException("Maximum marks must be positive, with at most two decimal places.");
    }
    public async Task<AssignmentResponse[]> List(Guid lesson,bool staff,CancellationToken token)
    {
        var p=await Access(lesson,staff,false,token);var list=await repo.List(lesson,token);var result=new List<AssignmentResponse>();
        foreach(var a in list)
        {
            if(a.LessonId!=lesson)throw new KeyNotFoundException("Assignment parent mismatch.");
            if(staff||(a.IsPublished&&p.CoursePublished&&p.LessonPublished)||(await repo.Submissions(a.Id,org.UserId,token)).Length>0)result.Add(await Map(a,token));
        }
        return result.ToArray();
    }
    public async Task<AssignmentResponse> Detail(Guid lesson,Guid id,bool staff,CancellationToken token)
    {
        var p=await Access(lesson,staff,false,token);var a=await Stored(lesson,id,token);
        if(!staff&&!(a.IsPublished&&p.CoursePublished&&p.LessonPublished)&&(await repo.Submissions(id,org.UserId,token)).Length==0)throw new KeyNotFoundException("Published assignment not found.");
        return await Map(a,token);
    }
    public async Task<AssignmentResponse> Create(Guid lesson,AssignmentRequest r,CancellationToken token)
    {await Access(lesson,true,false,token);Validate(r);var a=new LessonAssignment(lesson,r.Title,r.Instructions,r.DueAtUtc?.UtcDateTime,r.MaximumMarks,Now);repo.Add(a);await repo.Save(token);return await Map(a,token);}
    public Task<AssignmentResponse> Edit(Guid lesson,Guid id,AssignmentRequest r,CancellationToken token)
        =>repo.Locked(id,async ct=>{
            await Access(lesson,true,false,ct);var a=await Stored(lesson,id,ct);Validate(r);
            if(await repo.HasSubmissions(id,ct)&&(r.Instructions!=a.Instructions||r.DueAtUtc?.UtcDateTime!=a.DueAtUtc||r.MaximumMarks!=a.MaximumMarks))throw new InvalidOperationException("Instructions, due date and maximum marks are frozen once submissions exist.");
            a.Edit(r.Title,r.Instructions,r.DueAtUtc?.UtcDateTime,r.MaximumMarks,Now);repo.Update(a);await repo.Save(ct);return await Map(a,ct);
        },null,token);
    public Task<AssignmentResponse> Publish(Guid lesson,Guid id,PublicationRequest r,CancellationToken token)
        =>repo.Locked(id,async ct=>{await Access(lesson,true,false,ct);var a=await Stored(lesson,id,ct);if(!r.Confirmed)throw new ArgumentException("Explicit publication confirmation is required.");a.Publish(r.IsPublished,Now);repo.Update(a);await repo.Save(ct);return await Map(a,ct);},null,token);
    public async Task<SubmissionResponse> Submit(Guid lesson,Guid id,string? text,bool confirmed,string? filename,string? mediaType,long? length,Stream? source,CancellationToken token)
    {
        await Access(lesson,false,true,token);await Stored(lesson,id,token);
        if(!confirmed)throw new ArgumentException("Confirm the final submission. Resubmission is not supported.");
        text=string.IsNullOrWhiteSpace(text)?null:text;
        if(text?.Length>100000||text is null&&source is null)throw new ArgumentException("Provide text (up to 100000 characters), one PDF, or both.");
        IStagedMedia? staged=null;Guid? attachment=null;
        string? safeName=source is null?null:Path.GetFileName((filename??"attachment.pdf").Replace('\\','/'));
        if(source is not null&&(string.IsNullOrWhiteSpace(safeName)||safeName.Length>200||safeName.Any(char.IsControl)))throw new ArgumentException("Attachment filename is invalid or exceeds 200 characters.");
        try
        {
            if(source is not null)
            {
                var (type,limit)=MediaValidation.CheckDeclaration(filename??"",mediaType??"",length??0);
                if(type!=LessonContentType.Pdf)throw new ArgumentException("Only one PDF attachment is accepted.");
                attachment=Guid.NewGuid();staged=await storage.StageAsync(attachment.Value,source,limit,token);
                if(staged.Content.Length!=length)throw new ArgumentException("Declared and actual PDF lengths differ.");
                try {await MediaValidation.CheckSignatureAsync(staged.Content,LessonContentType.Pdf,token);}catch(EndOfStreamException){throw new ArgumentException("Truncated PDF.");}
            }
            return await repo.Locked(id,async ct=>{
                await Access(lesson,false,true,ct);var a=await Stored(lesson,id,ct);
                if(!a.IsPublished)throw new KeyNotFoundException("Published assignment not found.");
                if((await repo.Submissions(id,org.UserId,ct)).Length>0)throw new InvalidOperationException("A final submission already exists. Resubmission is not supported.");
                var now=Now;if(a.DueAtUtc.HasValue&&now>=a.DueAtUtc.Value)throw new InvalidOperationException("The assignment due date has been reached.");
                var submission=new AssignmentSubmission(a,org.UserId,text,attachment,safeName,source is null?null:length,now);
                if(staged is not null)await staged.PublishAsync(ct);
                repo.Add(submission);await repo.Save(ct);return await Map(submission,ct);
            },()=>staged?.Retain(),token);
        }
        finally {if(staged is not null)await staged.DisposeAsync();}
    }
    public async Task<SubmissionPage> StaffSubmissionPage(Guid lesson,Guid assignment,int page,int pageSize,CancellationToken token)
    {
        if(page is <1 or >10000 || pageSize is <1 or >50)throw new ArgumentException("Page must be 1-10000 and pageSize 1-50.");
        await Access(lesson,true,false,token);await Stored(lesson,assignment,token);
        var slice=await repo.SubmissionPage(assignment,page,pageSize,token);
        if(slice.Total<0 || slice.Items.Length>pageSize || slice.Items.Any(x=>x.AssignmentId!=assignment))throw new UnauthorizedAccessException("Submission parent mismatch.");
        var rows=new List<SubmissionResponse>();foreach(var item in slice.Items)rows.Add(await Map(item,token));
        return new(page,pageSize,slice.Total,rows.ToArray());
    }
    public async Task<SubmissionResponse[]> Submissions(Guid lesson,Guid assignment,bool staff,CancellationToken token)
    {await Access(lesson,staff,false,token);await Stored(lesson,assignment,token);var list=await repo.Submissions(assignment,staff?null:org.UserId,token);var output=new List<SubmissionResponse>();foreach(var s in list){if(s.AssignmentId!=assignment||!staff&&s.UserId!=org.UserId)throw new UnauthorizedAccessException("Submission ownership mismatch.");output.Add(await Map(s,token));}return output.ToArray();}
    public async Task<SubmissionResponse> Review(Guid lesson,Guid assignment,Guid submission,bool staff,CancellationToken token)
    {await Access(lesson,staff,false,token);await Stored(lesson,assignment,token);var s=await repo.Submission(submission,token);if(s is null||s.AssignmentId!=assignment)throw new KeyNotFoundException("Submission not found.");if(!staff&&s.UserId!=org.UserId)throw new UnauthorizedAccessException("Only the submission owner may review this work.");return await Map(s,token);}
    public Task<SubmissionResponse> Grade(Guid lesson,Guid assignment,Guid submission,GradeRequest r,CancellationToken token)
        =>repo.Locked(assignment,async ct=>{
            await Review(lesson,assignment,submission,true,ct);var s=(await repo.Submission(submission,ct))!;
            if(!r.Confirmed)throw new ArgumentException("Confirm the final grade.");
            if(await repo.Grade(submission,ct) is not null)throw new InvalidOperationException("This submission has already been graded. The original grade is retained.");
            if(r.Marks<0||r.Marks>s.MaximumMarksSnapshot||decimal.Round(r.Marks,2)!=r.Marks||r.Feedback is null||r.Feedback.Length>20000)throw new ArgumentException("Marks must be between zero and the submitted maximum, with at most two decimal places; feedback must fit 20000 characters.");
            repo.Add(new AssignmentGrade(submission,r.Marks,r.Feedback,org.UserId,Now));await repo.Save(ct);return await Map(s,ct);
        },null,token);
    public async Task<Stream> Attachment(Guid lesson,Guid assignment,Guid submission,bool staff,CancellationToken token)
    {await Review(lesson,assignment,submission,staff,token);var s=(await repo.Submission(submission,token))!;if(!s.AttachmentId.HasValue)throw new KeyNotFoundException("Attachment not found.");return await storage.OpenReadAsync(s.AttachmentId.Value,token);}
}
