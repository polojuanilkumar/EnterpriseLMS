using System.Reflection;
using LMS.Application.Features.Organizations;
using LMS.Application.Features.Reporting;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Repositories;
using Moq;
using Xunit;
namespace LMS.Tests;

// The same projection expressions run against in-memory sources for counting/policy checks.
// SQL translation is separately tested; neither establishes SQL Server execution behavior.
public sealed class ReportingPolicyTests
{
    internal sealed class Fixture
    {
        internal Guid Org=Guid.NewGuid(),OtherOrg=Guid.NewGuid(),Instructor=Guid.NewGuid(),Student=Guid.NewGuid(),OtherStudent=Guid.NewGuid();
        internal DateTime Now=new(2026,10,4,12,0,0,DateTimeKind.Utc);
        internal List<Course> Courses=[];internal List<CourseSection> Sections=[];internal List<Lesson> Lessons=[];internal List<CourseEnrollment> Enrollments=[];internal List<LessonProgress> Progress=[];internal List<Quiz> Quizzes=[];internal List<QuizAttempt> Attempts=[];internal List<LessonAssignment> Assignments=[];internal List<AssignmentSubmission> Work=[];internal List<AssignmentGrade> Grades=[];internal List<ReportName> Names=[];
        internal Course Course,Unassigned,Foreign;internal Lesson Lesson;internal Quiz Quiz;internal LessonAssignment GradedAssignment,EmptyAssignment,HiddenAssignment;internal AssignmentSubmission GradedWork;
        internal Fixture()
        {
            Course=AddCourse(Org,Instructor,"Alpha");Unassigned=AddCourse(Org,Guid.NewGuid(),"Beta");Foreign=AddCourse(OtherOrg,Instructor,"Foreign");
            Lesson=AddLesson(Course,true);var second=AddLesson(Course,true);var unpublished=AddLesson(Course,false);var foreignLesson=AddLesson(Foreign,true);
            Enrollments.Add(new(Course.Id,Student));var completed=new CourseEnrollment(Course.Id,OtherStudent);completed.Complete();Enrollments.Add(completed);
            var ownCompleted=new CourseEnrollment(Unassigned.Id,Student);ownCompleted.Complete();Enrollments.Add(ownCompleted);Enrollments.Add(new(Foreign.Id,Student));
            Complete(Student,Lesson);Complete(Student,Lesson);Complete(Student,unpublished);Complete(OtherStudent,second);
            Quiz=new(Lesson.Id,"Quiz",null,90,10,5);Quiz.Publish();Quizzes.Add(Quiz);
            AddAttempt(Quiz,Student,1,0,false);AddAttempt(Quiz,Student,2,7,true);AddAttempt(Quiz,Student,3,null,null,true);AddAttempt(Quiz,Student,4,null,null);
            AddAttempt(Quiz,OtherStudent,1,10,true);
            var unavailable=AddAttempt(Quiz,Student,5,1,false);typeof(QuizAttempt).GetProperty(nameof(QuizAttempt.EarnedMarks))!.SetValue(unavailable,null);
            var missingQuiz=new Quiz(second.Id,"Unattempted",null,50,5,1);missingQuiz.Publish();Quizzes.Add(missingQuiz);
            var foreignQuiz=new Quiz(foreignLesson.Id,"PRIVATE foreign quiz",null,50,10,1);foreignQuiz.Publish();Quizzes.Add(foreignQuiz);AddAttempt(foreignQuiz,Student,1,10,true);
            GradedAssignment=AddAssignment(Lesson,true,"Graded",10);GradedWork=AddWork(GradedAssignment,Student);Grades.Add(new(GradedWork.Id,0,"<script>Own feedback</script>",Instructor,Now));GradedAssignment.Edit("Edited title","Changed definition",null,99,Now);
            AddWork(AddAssignment(Lesson,true,"Ungraded",10),Student);EmptyAssignment=AddAssignment(Lesson,true,"Not submitted",12);
            HiddenAssignment=AddAssignment(Lesson,false,"PRIVATE other work",10);var other=AddWork(HiddenAssignment,OtherStudent);Grades.Add(new(other.Id,5,"PRIVATE other feedback",Instructor,Now));
            AddWork(AddAssignment(Lesson,false,"Historical",10),Student);
            var foreignWork=AddWork(AddAssignment(foreignLesson,true,"PRIVATE foreign assignment",10),Student);Grades.Add(new(foreignWork.Id,10,"PRIVATE foreign feedback",Instructor,Now));
            Names.Add(new(){Id=Student,FirstName="<script>Alice</script>",LastName="Learner"});Names.Add(new(){Id=OtherStudent,FirstName="Bob",LastName="Other"});
        }
        internal ReportingSources Sources=>new(Courses.AsQueryable(),Sections.AsQueryable(),Lessons.AsQueryable(),Enrollments.AsQueryable(),Progress.AsQueryable(),Quizzes.AsQueryable(),Attempts.AsQueryable(),Assignments.AsQueryable(),Work.AsQueryable(),Grades.AsQueryable(),Names.AsQueryable());
        internal OrganizationContext Scope(OrganizationRole role=OrganizationRole.Student,bool platform=false,Guid? organization=null,Guid? user=null)
        {var s=new OrganizationContext();s.Initialize(organization??Org,user??(role==OrganizationRole.Student?Student:Instructor),role,platform);return s;}
        internal ReportingQueries Query(OrganizationRole role=OrganizationRole.Student,bool platform=false,Guid? organization=null)=>new(Sources,Scope(role,platform,organization));
        internal Course AddCourse(Guid org,Guid owner,string title){var c=new Course(title,title,"",owner,CourseLevel.Beginner);c.AssignOrganization(org);c.Publish();Courses.Add(c);return c;}
        internal Lesson AddLesson(Course c,bool published){var s=new CourseSection(c.Id,"Section",null,0);Sections.Add(s);var l=new Lesson(s.Id,"Lesson",null,LessonType.Text,null,null,1,0);if(published)l.Publish();Lessons.Add(l);return l;}
        internal void Complete(Guid user,Lesson l){var p=new LessonProgress(user,l.Id);p.Complete();Progress.Add(p);}
        internal QuizAttempt AddAttempt(Quiz q,Guid user,int number,decimal? marks,bool? passed,bool expired=false)
        {var a=new QuizAttempt(q.Id,user,number,Now.AddDays(-2),Now.AddDays(-1),10,70);if(marks.HasValue)a.Submit(Now.AddDays(-1),marks.Value,passed!.Value);else if(expired)a.Expire(Now);Attempts.Add(a);return a;}
        internal LessonAssignment AddAssignment(Lesson l,bool published,string title,decimal maximum){var a=new LessonAssignment(l.Id,title,"Instructions",null,maximum,Now);if(published)a.Publish(true,Now);Assignments.Add(a);return a;}
        internal AssignmentSubmission AddWork(LessonAssignment a,Guid user){var s=new AssignmentSubmission(a,user,"PRIVATE submitted text",Guid.NewGuid(),"PRIVATE filename.pdf",100,Now);Work.Add(s);return s;}
    }
    [Theory][InlineData(OrganizationRole.Instructor,1)][InlineData(OrganizationRole.Admin,2)]
    public void Staff_queries_filter_assigned_courses_and_every_count_to_selected_org(OrganizationRole role,int count)
    {
        var f=new Fixture();var q=f.Query(role);var rows=q.Summary(new()).ToArray();Assert.Equal(count,rows.Length);Assert.All(rows,r=>Assert.Equal(f.Org,r.OrganizationId));Assert.DoesNotContain(rows,r=>r.CourseId==f.Foreign.Id);
        var row=Assert.Single(rows,r=>r.CourseId==f.Course.Id);Assert.Equal(2,row.Enrollments);Assert.Equal(1,row.Active);Assert.Equal(1,row.Completed);Assert.Equal(2,row.PublishedLessons);Assert.Equal(2,row.CompletedPublishedLearnerLessons);Assert.Equal(4,row.ProgressDenominator);Assert.Equal(50,row.CurrentPercentage);
        Assert.Equal(1,row.QuizStarted);Assert.Equal(1,row.QuizExpired);Assert.Equal(4,row.QuizSubmitted);Assert.Equal(2,row.QuizPassed);Assert.Equal(1,row.QuizFailed);Assert.Equal(1,row.QuizUnavailable);Assert.Equal(4,row.Submissions);Assert.Equal(2,row.Graded);Assert.Equal(2,row.Ungraded);
        Assert.Empty(q.Learners(f.Foreign.Id,new()));if(role==OrganizationRole.Instructor)Assert.Empty(q.Learners(f.Unassigned.Id,new()));
    }
    [Fact] public void Different_org_roles_same_actor_cannot_inherit_staff_permission()
    {
        var f=new Fixture();var admin=f.Query(OrganizationRole.Admin);Assert.Equal(2,admin.Summary(new()).Count());
        var studentScope=f.Scope(OrganizationRole.Student,organization:f.OtherOrg,user:f.Instructor);var learner=new ReportingQueries(f.Sources,studentScope);Assert.Throws<UnauthorizedAccessException>(()=>learner.Summary(new()));Assert.Empty(learner.GradeCourses(new()));
        Assert.Single(f.Query(OrganizationRole.Admin,organization:f.OtherOrg).Summary(new()));
    }
    [Fact] public void SuperAdmin_must_select_org_and_cannot_expand_totals_across_orgs()
    {
        var f=new Fixture();Assert.Equal(2,f.Query(OrganizationRole.Admin,true).Summary(new()).Count());Assert.Single(f.Query(OrganizationRole.Admin,true,f.OtherOrg).Summary(new()));
        var unset=new ReportingQueries(f.Sources,new());Assert.Throws<UnauthorizedAccessException>(()=>unset.Summary(new()));Assert.Throws<UnauthorizedAccessException>(()=>unset.GradeCourses(new()));
        Assert.Throws<UnauthorizedAccessException>(()=>f.Query(OrganizationRole.Admin,true).GradeCourses(new()));
    }
    [Fact] public void Filters_and_pagination_are_scoped_and_stably_ordered()
    {
        var f=new Fixture();var q=f.Query(OrganizationRole.Admin);Assert.Single(q.Summary(new(Search:"Alpha")));Assert.Empty(q.Summary(new(Search:"Foreign")));Assert.Single(q.Summary(new(EnrollmentStatus:1)));
        Assert.Single(q.Learners(f.Course.Id,new(EnrollmentStatus:2)));Assert.Single(q.Learners(f.Course.Id,new(Search:"Bob")));Assert.Empty(q.Learners(f.Course.Id,new(Search:"PRIVATE")));
        var filter=new ReportFilter(2,1);var rows=q.Summary(filter).OrderBy(x=>x.Title).ThenBy(x=>x.CourseId).Skip(filter.Skip).Take(filter.PageSize).ToArray();Assert.Equal("Beta",Assert.Single(rows).Title);Assert.Equal(2,q.Summary(filter).Count());
        f.Course.Archive();Assert.Single(q.Summary(new(Published:false)));Assert.Empty(q.Learners(f.Course.Id,new(Published:true)));
    }
    [Theory][InlineData(0,20)][InlineData(10001,20)][InlineData(1,0)][InlineData(1,51)]
    public void Pagination_is_bounded_before_queries(int page,int size)
    {var f=new Fixture();Assert.Throws<ArgumentException>(()=>f.Query(OrganizationRole.Admin).Summary(new(page,size)));Assert.Throws<ArgumentException>(()=>f.Query().Quizzes(f.Course.Id,new(page,size),f.Now));}
    [Fact] public void Unsupported_filters_are_rejected_and_empty_scopes_are_empty()
    {
        Assert.Throws<ArgumentException>(()=>new ReportFilter(Search:new string('x',101)).Validate());Assert.Throws<ArgumentException>(()=>new ReportFilter(EnrollmentStatus:3).Validate(true));Assert.Throws<ArgumentException>(()=>new AssessmentFilter(Status:4).Validate());
        var f=new Fixture();Assert.Throws<ArgumentException>(()=>f.Query().Assignments(f.Course.Id,new(Status:3)));Assert.Empty(f.Query(OrganizationRole.Admin,organization:Guid.NewGuid()).Summary(new()));
    }
    [Fact] public void Current_distinct_published_lesson_progress_is_separate_from_historical_completion()
    {
        var f=new Fixture();var row=Assert.Single(f.Query(OrganizationRole.Admin).Learners(f.Course.Id,new()),x=>x.UserId==f.OtherStudent);Assert.Equal(2,row.EnrollmentStatus);Assert.NotNull(row.EnrollmentCompletedAt);Assert.Equal(100,row.StoredEnrollmentPercentage);Assert.Equal(50,row.CurrentPercentage);Assert.False(row.CurrentContentComplete);
        foreach(var l in f.Lessons)l.Unpublish();var zero=Assert.Single(f.Query(OrganizationRole.Admin).Learners(f.Course.Id,new()),x=>x.UserId==f.OtherStudent);Assert.Equal(0,zero.CurrentPercentage);Assert.False(zero.CurrentContentComplete);Assert.Equal(row.EnrollmentCompletedAt,zero.EnrollmentCompletedAt);
    }
    [Fact] public void Own_gradebook_includes_active_completed_but_never_cancelled_or_foreign_courses()
    {
        var f=new Fixture();var q=f.Query();var rows=q.GradeCourses(new()).ToArray();Assert.Equal(2,rows.Length);Assert.All(rows,x=>Assert.Equal(f.Student,x.UserId));Assert.DoesNotContain(rows,x=>x.CourseId==f.Foreign.Id);Assert.Empty(q.Quizzes(f.Foreign.Id,new(),f.Now));Assert.Empty(q.Assignments(f.Foreign.Id,new()));
        var enrollment=f.Enrollments.Single(e=>e.CourseId==f.Course.Id&&e.UserId==f.Student);enrollment.Cancel();Assert.Single(q.GradeCourses(new()));Assert.Empty(q.Quizzes(f.Course.Id,new(),f.Now));Assert.Empty(q.Assignments(f.Course.Id,new()));
    }
    [Fact] public void Quiz_attempts_are_individual_snapshot_results_and_read_only_expiration()
    {
        var f=new Fixture();var before=f.Attempts.Select(a=>a.Status).ToArray();var rows=f.Query().Quizzes(f.Course.Id,new(),f.Now).ToArray();Assert.Equal(6,rows.Length);Assert.Equal(5,rows.Count(x=>x.AttemptId.HasValue));Assert.Single(rows,x=>x.Status==0);
        var submitted=rows.Where(x=>x.Status==2).ToArray();Assert.Equal(3,submitted.Length);Assert.Single(submitted,x=>x.EarnedMarks==0&&x.Passed==false&&x.ResultAvailable);Assert.Single(submitted,x=>!x.ResultAvailable);Assert.All(submitted,x=>{Assert.Equal(10,x.PossibleMarks);Assert.Equal(70,x.PassingPercentageSnapshot);});
        Assert.Single(rows,x=>x.Status==3);Assert.Single(rows,x=>x.Status==1&&x.Overdue);Assert.Equal(before,f.Attempts.Select(a=>a.Status));Assert.Single(f.Query().Quizzes(f.Course.Id,new(Status:3),f.Now));
    }
    [Fact] public void Assignment_rows_distinguish_graded_zero_ungraded_missing_and_snapshot_maximum()
    {
        var f=new Fixture();var rows=f.Query().Assignments(f.Course.Id,new()).ToArray();Assert.Equal(4,rows.Length);var graded=Assert.Single(rows,x=>x.Status==2);Assert.Equal(0,graded.Marks);Assert.Equal(10,graded.MaximumMarks);Assert.Equal(f.GradedWork.Id,graded.SubmissionId);Assert.Contains("Own feedback",graded.Feedback!);
        Assert.Equal(2,rows.Count(x=>x.Status==1));Assert.Single(rows,x=>x.Status==0&&x.MaximumMarks==12);Assert.DoesNotContain(rows,x=>x.Title.Contains("PRIVATE"));Assert.Single(f.Query().Assignments(f.Course.Id,new(Status:2)));
    }
    [Fact] public void Unpublishing_preserves_own_work_but_not_missing_assessment_discovery_or_detail_links()
    {
        var f=new Fixture();f.Course.Archive();f.Lesson.Unpublish();f.Quiz.Unpublish();var q=f.Query();Assert.Equal(2,q.GradeCourses(new()).Count());var quizzes=q.Quizzes(f.Course.Id,new(),f.Now).ToArray();Assert.Equal(5,quizzes.Length);Assert.All(quizzes,x=>Assert.False(x.DetailAvailable));
        var assignments=q.Assignments(f.Course.Id,new()).ToArray();Assert.Equal(3,assignments.Length);Assert.All(assignments,x=>Assert.NotNull(x.SubmissionId));Assert.All(assignments,x=>Assert.False(x.Published));
        f.Enrollments.Single(e=>e.CourseId==f.Course.Id&&e.UserId==f.Student).Complete();Assert.Equal(3,q.Assignments(f.Course.Id,new()).Count());
    }
    [Fact] public void Forged_stored_parent_links_do_not_pull_foreign_data_into_reports()
    {
        var f=new Fixture();var orphan=new LessonAssignment(Guid.NewGuid(),"Orphan","",null,10,f.Now);f.Assignments.Add(orphan);f.AddWork(orphan,f.Student);Assert.DoesNotContain(f.Query().Assignments(f.Course.Id,new()),x=>x.AssignmentId==orphan.Id);
        Assert.Empty(f.Query(OrganizationRole.Admin).StaffCourse(f.Foreign.Id));Assert.DoesNotContain(f.Query(OrganizationRole.Admin).Summary(new()),x=>x.CourseId==f.Foreign.Id);
    }
    [Fact] public async Task Handlers_recheck_role_scope_owner_and_nested_course_from_repository()
    {
        var f=new Fixture();var repo=new Mock<IReportingRepository>(MockBehavior.Strict);var scope=f.Scope();var handler=new ReportingHandlers(repo.Object,scope,TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>handler.Courses(new(),default));repo.VerifyNoOtherCalls();
        repo.Setup(x=>x.GradeCourse(f.Course.Id,default)).ReturnsAsync(new GradeCourseRow{CourseId=f.Course.Id,OrganizationId=f.Org,UserId=f.OtherStudent,EnrollmentStatus=1});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>handler.GradeCourse(f.Course.Id,default));
        var staffScope=f.Scope(OrganizationRole.Instructor);var staff=new ReportingHandlers(repo.Object,staffScope,TimeProvider.System);
        repo.Setup(x=>x.StaffCourse(f.Course.Id,default)).ReturnsAsync(new CourseReference(f.Course.Id,f.OtherOrg,f.Instructor,"Foreign",true));await Assert.ThrowsAsync<KeyNotFoundException>(()=>staff.Learners(f.Course.Id,new(),default));
        repo.Setup(x=>x.StaffCourse(f.Course.Id,default)).ReturnsAsync(new CourseReference(f.Course.Id,f.Org,Guid.NewGuid(),"Not assigned",true));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>staff.Learners(f.Course.Id,new(),default));
    }
    [Fact] public async Task Handlers_reject_foreign_totals_and_owner_labels_before_disclosure()
    {
        var f=new Fixture();var repo=new Mock<IReportingRepository>();var scope=f.Scope(OrganizationRole.Admin);var staff=new ReportingHandlers(repo.Object,scope,TimeProvider.System);
        repo.Setup(x=>x.Courses(It.IsAny<ReportFilter>(),default)).ReturnsAsync(new ReportPage<CourseReportRow>(f.OtherOrg,null,1,20,999,[]));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>staff.Courses(new(),default));
        var learner=new ReportingHandlers(repo.Object,f.Scope(),TimeProvider.System);repo.Setup(x=>x.GradeCourses(It.IsAny<ReportFilter>(),default)).ReturnsAsync(new ReportPage<GradeCourseRow>(f.Org,f.OtherStudent,1,20,1,[]));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>learner.GradeCourses(new(),default));
        var unset=new ReportingHandlers(repo.Object,new(),TimeProvider.System);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>unset.Courses(new(),default));
    }
    [Fact] public void Queries_do_not_change_progress_enrollment_grade_or_attempt_entities()
    {
        var f=new Fixture();var accessed=f.Progress.Select(p=>p.LastAccessedAt).ToArray();var states=f.Enrollments.Select(e=>(e.Status,e.CompletedAt)).ToArray();var grades=f.Grades.Select(g=>(g.Marks,g.Feedback,g.GradedAt)).ToArray();
        f.Query(OrganizationRole.Admin).Summary(new()).ToArray();f.Query(OrganizationRole.Admin).Learners(f.Course.Id,new()).ToArray();f.Query().GradeCourses(new()).ToArray();f.Query().Quizzes(f.Course.Id,new(),f.Now).ToArray();f.Query().Assignments(f.Course.Id,new()).ToArray();
        Assert.Equal(accessed,f.Progress.Select(p=>p.LastAccessedAt));Assert.Equal(states,f.Enrollments.Select(e=>(e.Status,e.CompletedAt)));Assert.Equal(grades,f.Grades.Select(g=>(g.Marks,g.Feedback,g.GradedAt)));
    }
    [Fact] public void Staff_keeps_cancelled_history_and_review_links_use_stored_authorized_parents()
    {
        var f=new Fixture();f.Enrollments.Single(e=>e.CourseId==f.Course.Id&&e.UserId==f.Student).Cancel();var q=f.Query(OrganizationRole.Instructor);var summary=Assert.Single(q.Summary(new()));Assert.Equal(1,summary.Cancelled);Assert.Equal(4,summary.Submissions);
        var row=Assert.Single(q.Learners(f.Course.Id,new(EnrollmentStatus:3)));Assert.Equal(f.Student,row.UserId);Assert.NotNull(row.RecentAssignmentReview);var link=row.RecentAssignmentReview!;
        Assert.Equal(f.Course.Id,link.CourseId);var lesson=Assert.Single(f.Lessons,l=>l.Id==link.LessonId);Assert.Equal(lesson.SectionId,link.SectionId);Assert.Equal(link.AssignmentId,Assert.Single(f.Work,s=>s.Id==link.SubmissionId).AssignmentId);Assert.Empty(f.Query().Assignments(f.Course.Id,new()));
    }
}
