using LMS.Application.Features.Assignments;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Lessons;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Xunit;
namespace LMS.Tests;

// In-memory policy tests: they do not verify SQL locks, rollback, or real concurrency.
public sealed class AssignmentPolicyTests
{
    [Fact] public async Task Authoring_requires_positive_marks_valid_text_and_explicit_publication_confirmation()
    {
        var f=new Fixture(OrganizationRole.Instructor);
        var created=await f.Handler.Create(f.Parent.LessonId,f.Request(),default);
        Assert.False(created.IsPublished);Assert.Equal(f.Clock.Now,created.CreatedAt);
        Assert.Equal(f.Parent.LessonId,created.LessonId);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Create(f.Parent.LessonId,f.Request(marks:0),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Create(f.Parent.LessonId,new(){Title="",Instructions="Work",MaximumMarks=1},default));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Publish(f.Parent.LessonId,f.A.Id,new(true,false),default));
        await f.Handler.Publish(f.Parent.LessonId,f.A.Id,new(false,true),default);Assert.False(f.A.IsPublished);
        await f.Handler.Publish(f.Parent.LessonId,f.A.Id,new(true,true),default);Assert.True(f.A.IsPublished);
    }
    [Theory][InlineData(OrganizationRole.Instructor,false,true)][InlineData(OrganizationRole.Instructor,true,false)][InlineData(OrganizationRole.Admin,true,true)]
    public async Task Staff_authorization_uses_stored_course_owner(OrganizationRole role,bool foreignOwner,bool allowed)
    {
        var f=new Fixture(role);if(foreignOwner)f.Repo.ParentValue=f.Parent with {InstructorId=Guid.NewGuid()};
        if(allowed)await f.Handler.Create(f.Parent.LessonId,f.Request(),default);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Create(f.Parent.LessonId,f.Request(),default));
    }
    [Fact] public async Task SuperAdmin_requires_explicit_org_and_foreign_or_mismatched_parents_are_rejected()
    {
        var f=new Fixture(OrganizationRole.Admin,true);await f.Handler.Create(f.Parent.LessonId,f.Request(),default);
        var none=new OrganizationContext();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new AssignmentHandlers(f.Repo,f.Storage,none,f.Clock).List(f.Parent.LessonId,true,default));
        f.Repo.ParentValue=f.Parent with {OrganizationId=Guid.NewGuid()};
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.List(f.Parent.LessonId,true,default));
        f.Repo.ParentValue=f.Parent with {LessonId=Guid.NewGuid()};
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.List(f.Parent.LessonId,true,default));
        f.Repo.ParentValue=f.Parent;
        var other=new LessonAssignment(Guid.NewGuid(),"Other","Other",null,10,f.Clock.Now.UtcDateTime);f.Repo.Assignments.Add(other);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Edit(f.Parent.LessonId,other.Id,f.Request(),default));
    }
    [Theory][InlineData("instructions")][InlineData("due")][InlineData("marks")]
    public async Task Submissions_freeze_rules_but_title_edit_and_unpublish_retain_history(string field)
    {
        var f=new Fixture(OrganizationRole.Instructor);var s=f.Work();var request=f.Request();
        if(field=="instructions")request.Instructions="Changed";if(field=="due")request.DueAtUtc=f.Clock.Now.AddDays(1);if(field=="marks")request.MaximumMarks=11;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Handler.Edit(f.Parent.LessonId,f.A.Id,request,default));
        var title=f.Request();title.Title="Renamed";var result=await f.Handler.Edit(f.Parent.LessonId,f.A.Id,title,default);Assert.True(result.Frozen);Assert.Equal("Renamed",result.Title);
        await f.Handler.Publish(f.Parent.LessonId,f.A.Id,new(false,true),default);Assert.Single(f.Repo.Work);
        Assert.Equal(s.InstructionsSnapshot,(await f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,true,default)).InstructionsSnapshot);
        Assert.True(f.Repo.LockCalls>=3);
    }
    [Theory][InlineData(-1,true)][InlineData(0,false)][InlineData(1,false)]
    public async Task Deadline_is_server_time_and_inclusive(long offsetSeconds,bool allowed)
    {
        var f=new Fixture();f.A.Edit("Assignment","Instructions",f.Clock.Now.AddSeconds(-offsetSeconds).UtcDateTime,10,f.Clock.Now.UtcDateTime);
        if(allowed){var result=await f.Submit("Final");Assert.Equal(f.Clock.Now,result.SubmittedAt);Assert.Equal(10,result.MaximumMarksSnapshot);Assert.Equal("Instructions",result.InstructionsSnapshot);}
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Submit("Final"));
    }
    [Fact] public async Task Duplicate_final_submission_is_conflict_and_original_is_retained()
    {
        var f=new Fixture();var first=await f.Submit("Original");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Submit("Replacement"));
        Assert.Single(f.Repo.Work);Assert.Equal(first.Id,f.Repo.Work[0].Id);Assert.Equal("Original",f.Repo.Work[0].Text);
        Assert.DoesNotContain(typeof(SubmissionResponse).GetProperties(),p=>p.Name is "AttachmentId" or "StoragePath");
    }
    [Fact] public async Task Final_confirmation_and_nonempty_work_are_required()
    {
        var f=new Fixture();await Assert.ThrowsAsync<ArgumentException>(()=>f.Submit("Work",false));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Submit("  "));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Submit(new string('x',100001)));
        Assert.Empty(f.Repo.Work);
    }
    [Theory][InlineData(1,true,true)][InlineData(2,false,true)][InlineData(3,false,false)][InlineData(0,false,false)]
    public async Task Enrollment_states_keep_reads_active_or_completed_and_mutations_active_only(int state,bool submit,bool read)
    {
        var f=new Fixture();f.Repo.EnrollmentState=state;var s=f.Work();
        if(read)Assert.Equal(s.Id,(await f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,false,default)).Id);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,false,default));
        f.Repo.Work.Clear();
        if(submit)await f.Submit("Work");else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Submit("Work"));
    }
    [Fact] public async Task Unpublished_chain_blocks_submit_but_preserves_completed_owner_history()
    {
        var f=new Fixture();var s=f.Work();f.Repo.EnrollmentState=2;
        f.A.Publish(false,f.Clock.Now.UtcDateTime);f.Repo.ParentValue=f.Parent with {CoursePublished=false,LessonPublished=false};
        Assert.Single(await f.Handler.List(f.Parent.LessonId,false,default));Assert.Equal(s.Id,(await f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,false,default)).Id);
        Assert.Single(await f.Handler.History(default));
        f.Repo.EnrollmentState=3;Assert.Empty(await f.Handler.History(default));
        f.Repo.EnrollmentState=1;f.Repo.Work.Clear();
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Submit("Late"));
        f.Repo.ParentValue=f.Parent;
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Submit("Unpublished assignment"));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Detail(f.Parent.LessonId,f.A.Id,false,default));
    }
    [Fact] public async Task Student_cannot_use_staff_flows_or_read_another_students_work_or_attachments()
    {
        var f=new Fixture();var other=f.Work(Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Review(f.Parent.LessonId,f.A.Id,other.Id,false,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Attachment(f.Parent.LessonId,f.A.Id,other.Id,false,default));
        Assert.Empty(await f.Handler.Submissions(f.Parent.LessonId,f.A.Id,false,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Create(f.Parent.LessonId,f.Request(),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Grade(f.Parent.LessonId,f.A.Id,other.Id,new(1,"",true),default));Assert.Equal(0,f.Storage.OpenCalls);
    }
    [Theory][InlineData(-0.01)][InlineData(10.01)][InlineData(0.001)]
    public async Task Grading_rejects_out_of_bounds_or_excess_precision(double marks)
    {
        var f=new Fixture(OrganizationRole.Instructor);var s=f.Work();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new((decimal)marks,"Feedback",true),default));Assert.Empty(f.Repo.Grades);
    }
    [Theory][InlineData(0)][InlineData(10)]
    public async Task Grade_once_inclusive_bounds_server_timestamp_and_snapshot(double marks)
    {
        var f=new Fixture(OrganizationRole.Admin);var s=f.Work();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new((decimal)marks,"",false),default));
        var result=await f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new((decimal)marks,"Original feedback",true),default);
        Assert.Equal((decimal)marks,result.Grade!.Marks);Assert.Equal(f.Org.UserId,result.Grade.GraderId);Assert.Equal(f.Clock.Now,result.Grade.GradedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new(1,"Overwrite",true),default));Assert.Single(f.Repo.Grades);Assert.Equal("Original feedback",f.Repo.Grades[0].Feedback);
    }
    [Fact] public async Task Submission_ids_must_match_assignment_and_lesson_for_review_grade_and_download()
    {
        var f=new Fixture(OrganizationRole.Admin);var another=new LessonAssignment(f.Parent.LessonId,"Other","Other",null,2,f.Clock.Now.UtcDateTime);f.Repo.Assignments.Add(another);
        var s=new AssignmentSubmission(another,f.Org.UserId,"Other",null,null,null,f.Clock.Now.UtcDateTime);f.Repo.Work.Add(s);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,true,default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new(1,"",true),default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Attachment(f.Parent.LessonId,f.A.Id,s.Id,true,default));
    }
    [Theory][InlineData(OrganizationRole.Admin)][InlineData(OrganizationRole.Instructor)]
    public async Task Authorized_staff_can_review_download_and_grade_cancelled_students_historical_work(OrganizationRole role)
    {
        var f=new Fixture(role);f.Repo.EnrollmentState=3;f.Repo.ParentValue=f.Parent with {CoursePublished=false,LessonPublished=false};f.A.Publish(false,f.Clock.Now.UtcDateTime);
        var s=new AssignmentSubmission(f.A,Guid.NewGuid(),"Final",Guid.NewGuid(),"work.pdf",100,f.Clock.Now.UtcDateTime);f.Repo.Work.Add(s);
        Assert.Equal(s.Id,(await f.Handler.Review(f.Parent.LessonId,f.A.Id,s.Id,true,default)).Id);
        using var stream=await f.Handler.Attachment(f.Parent.LessonId,f.A.Id,s.Id,true,default);Assert.Equal(1,f.Storage.OpenCalls);
        Assert.NotNull((await f.Handler.Grade(f.Parent.LessonId,f.A.Id,s.Id,new(10,"Final feedback",true),default)).Grade);
        if(role==OrganizationRole.Instructor){f.Repo.ParentValue=f.Repo.ParentValue with {InstructorId=Guid.NewGuid()};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Handler.Attachment(f.Parent.LessonId,f.A.Id,s.Id,true,default));Assert.Equal(1,f.Storage.OpenCalls);}
    }
    [Fact] public async Task UTC_due_conversion_both_work_sources_and_declared_length_validation()
    {
        var author=new Fixture(OrganizationRole.Admin);var request=author.Request();request.DueAtUtc=new DateTimeOffset(2026,10,4,17,0,0,TimeSpan.FromMinutes(330));
        var a=await author.Handler.Create(author.Parent.LessonId,request,default);Assert.Equal(TimeSpan.Zero,a.DueAtUtc!.Value.Offset);Assert.Equal(11,a.DueAtUtc.Value.Hour);Assert.Equal(30,a.DueAtUtc.Value.Minute);
        var student=new Fixture();using var source=Pdf();var result=await student.Handler.Submit(student.Parent.LessonId,student.A.Id,"Text plus PDF",true,"work.pdf","application/pdf",source.Length,source,default);Assert.Equal("Text plus PDF",result.Text);Assert.True(result.HasAttachment);
        var mismatch=new Fixture();using var other=Pdf();await Assert.ThrowsAsync<ArgumentException>(()=>mismatch.Handler.Submit(mismatch.Parent.LessonId,mismatch.A.Id,null,true,"work.pdf","application/pdf",other.Length+1,other,default));Assert.True(mismatch.Storage.Last!.Disposed);Assert.False(mismatch.Storage.Last.Retained);
    }
    [Theory][InlineData("bad.pdf","application/pdf",true)][InlineData("bad.mp3","audio/mpeg",false)][InlineData("bad.pdf","text/html",false)]
    public async Task Invalid_attachment_is_rejected_and_handled_staging_is_cleaned(string name,string mime,bool staged)
    {
        var f=new Fixture();using var source=new MemoryStream("not a PDF"u8.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Handler.Submit(f.Parent.LessonId,f.A.Id,null,true,name,mime,source.Length,source,default));
        Assert.Empty(f.Repo.Work);Assert.Equal(staged,f.Storage.Last is not null);if(staged){Assert.True(f.Storage.Last!.Disposed);Assert.False(f.Storage.Last.Retained);}
    }
    [Fact] public async Task Pdf_only_submission_uses_generated_private_key_safe_filename_and_retains_committed_file()
    {
        var f=new Fixture();using var source=Pdf();
        var result=await f.Handler.Submit(f.Parent.LessonId,f.A.Id,null,true,"C:\\fakepath\\<work>.pdf","application/pdf",source.Length,source,default);
        Assert.Equal("<work>.pdf",result.Filename);Assert.True(result.HasAttachment);Assert.Null(result.Text);Assert.True(f.Storage.Last!.Published);Assert.True(f.Storage.Last.Retained);Assert.True(f.Storage.Last.Disposed);
        using var downloaded=await f.Handler.Attachment(f.Parent.LessonId,f.A.Id,result.Id,false,default);Assert.Equal(1,f.Storage.OpenCalls);
        Assert.Equal(f.Repo.Work[0].AttachmentId,f.Storage.Key);Assert.NotEqual(Guid.Empty,f.Storage.Key);
    }
    [Fact] public async Task Revalidation_after_upload_rejects_changed_publication_and_cleanup_occurs()
    {
        var f=new Fixture();f.Storage.OnStage=()=>f.Repo.ParentValue=f.Parent with {CoursePublished=false};using var source=Pdf();
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Handler.Submit(f.Parent.LessonId,f.A.Id,"Work",true,"work.pdf","application/pdf",source.Length,source,default));
        Assert.False(f.Storage.Last!.Retained);Assert.True(f.Storage.Last.Disposed);Assert.Empty(f.Repo.Work);
    }
    [Fact] public async Task Persistence_failure_cleans_published_file_but_uncertain_commit_retains_it_without_replay()
    {
        var failed=new Fixture();failed.Repo.FailSave=true;using var a=Pdf();
        await Assert.ThrowsAsync<IOException>(()=>failed.Handler.Submit(failed.Parent.LessonId,failed.A.Id,null,true,"a.pdf","application/pdf",a.Length,a,default));
        Assert.True(failed.Storage.Last!.Published);Assert.True(failed.Storage.Last.Disposed);Assert.False(failed.Storage.Last.Retained);
        var uncertain=new Fixture();uncertain.Repo.FailCommit=true;using var b=Pdf();
        await Assert.ThrowsAsync<IOException>(()=>uncertain.Handler.Submit(uncertain.Parent.LessonId,uncertain.A.Id,null,true,"b.pdf","application/pdf",b.Length,b,default));
        Assert.True(uncertain.Storage.Last!.Retained);Assert.Equal(1,uncertain.Repo.LockCalls);
    }
    [Fact] public async Task Due_time_is_rechecked_after_staging_and_pdf_size_is_bounded_before_staging()
    {
        var f=new Fixture();f.A.Edit("Assignment","Instructions",f.Clock.Now.AddMinutes(1).UtcDateTime,10,f.Clock.Now.UtcDateTime);f.Storage.OnStage=()=>f.Clock.Now=f.Clock.Now.AddMinutes(1);using var source=Pdf();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Handler.Submit(f.Parent.LessonId,f.A.Id,null,true,"a.pdf","application/pdf",source.Length,source,default));Assert.True(f.Storage.Last!.Disposed);Assert.False(f.Storage.Last.Retained);
        var limit=new Fixture();using var other=Pdf();await Assert.ThrowsAsync<ArgumentException>(()=>limit.Handler.Submit(limit.Parent.LessonId,limit.A.Id,null,true,"a.pdf","application/pdf",26214401,other,default));Assert.Null(limit.Storage.Last);
    }
    internal static MemoryStream Pdf()=>new("%PDF-1.7\ncontent\n%%EOF\n"u8.ToArray());
    internal sealed class Fixture
    {
        public LearnerWebTests.Clock Clock{get;}=new(){Now=new DateTimeOffset(2026,10,4,9,0,0,TimeSpan.Zero)};
        public OrganizationContext Org{get;}=new();public AssignmentParent Parent{get;}public LessonAssignment A{get;}public Repo Repo{get;}public Storage Storage{get;}=new();public AssignmentHandlers Handler{get;}
        public Fixture(OrganizationRole role=OrganizationRole.Student,bool platform=false)
        {var user=Guid.NewGuid();var org=Guid.NewGuid();Org.Initialize(org,user,role,platform);Parent=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),org,user,true,true);A=new(Parent.LessonId,"Assignment","Instructions",null,10,Clock.Now.UtcDateTime);A.Publish(true,Clock.Now.UtcDateTime);Repo=new(Parent,A);Handler=new(Repo,Storage,Org,Clock);}
        public AssignmentRequest Request(decimal marks=10)=>new(){Title="Assignment",Instructions="Instructions",MaximumMarks=marks};
        public AssignmentSubmission Work(Guid? user=null){var s=new AssignmentSubmission(A,user??Org.UserId,"Original",null,null,null,Clock.Now.UtcDateTime);Repo.Work.Add(s);return s;}
        public Task<SubmissionResponse> Submit(string? text,bool confirmed=true)=>Handler.Submit(Parent.LessonId,A.Id,text,confirmed,null,null,null,null,default);
    }
    internal sealed class Repo(AssignmentParent parent,LessonAssignment assignment):IAssignmentRepository
    {
        public AssignmentParent ParentValue=parent;public int? EnrollmentState=1;public List<LessonAssignment> Assignments=[assignment];public List<AssignmentSubmission> Work=[];public List<AssignmentGrade> Grades=[];public int LockCalls,SaveCalls;public bool FailSave,FailCommit;
        public Task<AssignmentParent?> Parent(Guid id,CancellationToken t)=>Task.FromResult<AssignmentParent?>(ParentValue);
        public Task<int?> Enrollment(Guid c,Guid u,CancellationToken t)=>Task.FromResult(EnrollmentState);
        public Task<LessonAssignment?> Get(Guid id,CancellationToken t)=>Task.FromResult(Assignments.SingleOrDefault(x=>x.Id==id));
        public Task<LessonAssignment[]> List(Guid id,CancellationToken t)=>Task.FromResult(Assignments.Where(x=>x.LessonId==id).ToArray());
        public Task<AssignmentSubmission[]> Submissions(Guid id,Guid? u,CancellationToken t)=>Task.FromResult(Work.Where(x=>x.AssignmentId==id&&(!u.HasValue||x.UserId==u)).ToArray());
        public Task<bool> HasSubmissions(Guid id,CancellationToken t)=>Task.FromResult(Work.Any(x=>x.AssignmentId==id));
        public Task<SubmissionSlice> SubmissionPage(Guid id,int page,int pageSize,CancellationToken t){var rows=Work.Where(x=>x.AssignmentId==id).OrderByDescending(x=>x.SubmittedAt).ThenBy(x=>x.Id).ToArray();return Task.FromResult(new SubmissionSlice(rows.Length,rows.Skip((page-1)*pageSize).Take(pageSize).ToArray()));}
        public Task<AssignmentSubmission?> Submission(Guid id,CancellationToken t)=>Task.FromResult(Work.SingleOrDefault(x=>x.Id==id));
        public Task<AssignmentGrade?> Grade(Guid id,CancellationToken t)=>Task.FromResult(Grades.SingleOrDefault(x=>x.SubmissionId==id));
        public Task<AssignmentSubmission[]> History(Guid u,CancellationToken t)=>Task.FromResult(Work.Where(x=>x.UserId==u).ToArray());
        public void Add(object o){if(o is LessonAssignment a)Assignments.Add(a);else if(o is AssignmentSubmission s)Work.Add(s);else if(o is AssignmentGrade g)Grades.Add(g);}
        public void Update(LessonAssignment a){}
        public Task Save(CancellationToken t){SaveCalls++;if(FailSave)throw new IOException("Simulated persistence failure");return Task.CompletedTask;}
        public async Task<T> Locked<T>(Guid id,Func<CancellationToken,Task<T>> action,Action? beforeCommit,CancellationToken t){LockCalls++;var r=await action(t);beforeCommit?.Invoke();if(FailCommit)throw new IOException("Simulated uncertain commit");return r;}
    }
    internal sealed class Storage:IAssignmentStorage
    {
        public Stage? Last;public Guid Key;public int OpenCalls;public Action? OnStage;
        public async Task<IStagedMedia> StageAsync(Guid id,Stream source,long limit,CancellationToken t){Key=id;var copy=new MemoryStream();await source.CopyToAsync(copy,t);copy.Position=0;Last=new(copy);OnStage?.Invoke();return Last;}
        public Task<Stream> OpenReadAsync(Guid id,CancellationToken t){OpenCalls++;return Task.FromResult<Stream>(Pdf());}
    }
    internal sealed class Stage(MemoryStream content):IStagedMedia
    {public bool Published,Retained,Disposed;public Stream Content=>content;public Task PublishAsync(CancellationToken t){Published=true;return Task.CompletedTask;}public void Retain()=>Retained=true;public ValueTask DisposeAsync(){Disposed=true;content.Dispose();return ValueTask.CompletedTask;}}
}
