using LMS.Application.Features.Organizations;
using LMS.Application.Features.Reporting;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace LMS.Infrastructure.Repositories;

internal sealed record ReportName
{public Guid Id{get;init;}public string FirstName{get;init;}="";public string LastName{get;init;}="";}
internal sealed record ReportingSources(IQueryable<Course> Courses,IQueryable<CourseSection> Sections,IQueryable<Lesson> Lessons,IQueryable<CourseEnrollment> Enrollments,IQueryable<LessonProgress> Progress,IQueryable<Quiz> Quizzes,IQueryable<QuizAttempt> Attempts,IQueryable<LessonAssignment> Assignments,IQueryable<AssignmentSubmission> Submissions,IQueryable<AssignmentGrade> Grades,IQueryable<ReportName> Names)
{
    internal static ReportingSources From(LMSDbContext db)=>new(db.Courses.AsNoTracking(),db.CourseSections.AsNoTracking(),db.Lessons.AsNoTracking(),db.CourseEnrollments.AsNoTracking(),db.LessonProgresses.AsNoTracking(),db.Quizzes.AsNoTracking(),db.QuizAttempts.AsNoTracking(),db.LessonAssignments.AsNoTracking(),db.AssignmentSubmissions.AsNoTracking(),db.AssignmentGrades.AsNoTracking(),db.Users.AsNoTracking().Select(u=>new ReportName{Id=u.Id,FirstName=u.FirstName,LastName=u.LastName}));
}
internal sealed class ReportingQueries(ReportingSources source,OrganizationContext org)
{
    internal IQueryable<Course> Courses(bool staff)
    {
        if(!org.OrganizationId.HasValue||org.UserId==Guid.Empty|| (staff?!org.CanManageContent:org.IsSuperAdmin||org.Role!=OrganizationRole.Student))throw new UnauthorizedAccessException("Verified selected organization role is required.");
        var id=org.OrganizationId.Value;var user=org.UserId;var instructor=staff&&!org.IsSuperAdmin&&org.Role==OrganizationRole.Instructor;
        var rows=source.Courses.Where(c=>c.OrganizationId==id&&(!instructor||c.InstructorId==user));
        if(!staff)rows=rows.Where(c=>source.Enrollments.Any(e=>e.CourseId==c.Id&&e.UserId==user&&(e.Status==EnrollmentStatus.Active||e.Status==EnrollmentStatus.Completed)));
        return rows;
    }
    private IQueryable<Lesson> Lessons(bool staff)
    {var courses=Courses(staff);return source.Lessons.Where(l=>source.Sections.Any(s=>s.Id==l.SectionId&&courses.Any(c=>c.Id==s.CourseId)));}
    private IQueryable<Course> Filter(ReportFilter f,bool staff)
    {
        f.Validate(!staff);var rows=Courses(staff);var search=f.Search?.Trim();
        if(!string.IsNullOrEmpty(search))rows=rows.Where(c=>c.Title.Contains(search));if(f.Published.HasValue)rows=rows.Where(c=>c.IsPublished==f.Published.Value);
        if(f.EnrollmentStatus.HasValue){var user=org.UserId;rows=rows.Where(c=>source.Enrollments.Any(e=>e.CourseId==c.Id&&(int)e.Status==f.EnrollmentStatus.Value&&(staff||e.UserId==user)));}return rows;
    }
    internal IQueryable<CourseReference> StaffCourse(Guid id)=>Courses(true).Where(c=>c.Id==id).Select(c=>new CourseReference(c.Id,c.OrganizationId!.Value,c.InstructorId,c.Title,c.IsPublished));
    internal IQueryable<CourseReportRow> Summary(ReportFilter filter)
    {
        var courses=Filter(filter,true);var lessons=Lessons(true);
        var attempts=from a in source.Attempts join q in source.Quizzes on a.QuizId equals q.Id join l in lessons on q.LessonId equals l.Id join s in source.Sections on l.SectionId equals s.Id select new{a.Status,a.IsPassed,a.EarnedMarks,s.CourseId};
        var assignments=from a in source.Assignments join l in lessons on a.LessonId equals l.Id join s in source.Sections on l.SectionId equals s.Id select new{a.Id,s.CourseId};
        var submissions=from sub in source.Submissions join a in assignments on sub.AssignmentId equals a.Id select new{sub.Id,a.CourseId,Graded=source.Grades.Any(g=>g.SubmissionId==sub.Id)};
        var completedPairs=(from e in source.Enrollments join c in Courses(true) on e.CourseId equals c.Id join s in source.Sections on c.Id equals s.CourseId join l in lessons on s.Id equals l.SectionId where l.IsPublished&&source.Progress.Any(p=>p.UserId==e.UserId&&p.LessonId==l.Id&&p.IsCompleted) select new{CourseId=c.Id,e.UserId,LessonId=l.Id}).Distinct();
        return courses.Select(c=>new CourseReportRow{
            CourseId=c.Id,OrganizationId=c.OrganizationId!.Value,InstructorId=c.InstructorId,Title=c.Title,Published=c.IsPublished,
            Enrollments=source.Enrollments.Count(e=>e.CourseId==c.Id),Active=source.Enrollments.Count(e=>e.CourseId==c.Id&&e.Status==EnrollmentStatus.Active),Completed=source.Enrollments.Count(e=>e.CourseId==c.Id&&e.Status==EnrollmentStatus.Completed),Cancelled=source.Enrollments.Count(e=>e.CourseId==c.Id&&e.Status==EnrollmentStatus.Cancelled),
            PublishedLessons=lessons.Count(l=>l.IsPublished&&source.Sections.Any(s=>s.Id==l.SectionId&&s.CourseId==c.Id)),
            CompletedPublishedLearnerLessons=completedPairs.Count(p=>p.CourseId==c.Id),
            QuizStarted=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Started),QuizSubmitted=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Submitted),QuizExpired=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Expired),
            QuizPassed=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Submitted&&a.EarnedMarks.HasValue&&a.IsPassed==true),QuizFailed=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Submitted&&a.EarnedMarks.HasValue&&a.IsPassed==false),QuizUnavailable=attempts.Count(a=>a.CourseId==c.Id&&a.Status==QuizAttemptStatus.Submitted&&(!a.EarnedMarks.HasValue||!a.IsPassed.HasValue)),
            Assignments=assignments.Count(a=>a.CourseId==c.Id),Submissions=submissions.Count(s=>s.CourseId==c.Id),Graded=submissions.Count(s=>s.CourseId==c.Id&&s.Graded),Ungraded=submissions.Count(s=>s.CourseId==c.Id&&!s.Graded)
        });
    }
    internal IQueryable<LearnerReportRow> Learners(Guid course,ReportFilter filter)
    {
        filter.Validate();var courses=Courses(true).Where(c=>c.Id==course);var lessons=Lessons(true).Where(l=>source.Sections.Any(s=>s.Id==l.SectionId&&s.CourseId==course));
        var attempts=from a in source.Attempts join q in source.Quizzes on a.QuizId equals q.Id join l in lessons on q.LessonId equals l.Id select a;
        var submissions=from sub in source.Submissions join a in source.Assignments on sub.AssignmentId equals a.Id join l in lessons on a.LessonId equals l.Id select sub;
        var reviewLinks=from sub in source.Submissions join a in source.Assignments on sub.AssignmentId equals a.Id join l in lessons on a.LessonId equals l.Id join s in source.Sections on l.SectionId equals s.Id select new{sub.UserId,sub.Id,sub.SubmittedAt,AssignmentId=a.Id,LessonId=l.Id,SectionId=s.Id,s.CourseId};
        var enrollments=source.Enrollments.Where(e=>courses.Any(c=>c.Id==e.CourseId));if(filter.EnrollmentStatus.HasValue)enrollments=enrollments.Where(e=>(int)e.Status==filter.EnrollmentStatus.Value);
        var names=source.Names;var search=filter.Search?.Trim();
        var rows=enrollments;
        if(!string.IsNullOrEmpty(search))rows=rows.Where(e=>names.Any(u=>u.Id==e.UserId&&(u.FirstName.Contains(search)||u.LastName.Contains(search))));
        // Publication filter applies to the stored course, not individual historical records.
        if(filter.Published.HasValue)rows=rows.Where(e=>courses.Any(c=>c.Id==e.CourseId&&c.IsPublished==filter.Published.Value));
        return rows.Select(e=>new LearnerReportRow{
            CourseId=e.CourseId,UserId=e.UserId,FirstName=names.Where(u=>u.Id==e.UserId).Select(u=>u.FirstName).FirstOrDefault()??"",LastName=names.Where(u=>u.Id==e.UserId).Select(u=>u.LastName).FirstOrDefault()??"",EnrollmentStatus=(int)e.Status,EnrolledAt=e.EnrolledAt,EnrollmentCompletedAt=e.CompletedAt,StoredEnrollmentPercentage=e.ProgressPercentage,
            PublishedLessons=lessons.Count(l=>l.IsPublished),CompletedPublishedLessons=lessons.Count(l=>l.IsPublished&&source.Progress.Any(p=>p.LessonId==l.Id&&p.UserId==e.UserId&&p.IsCompleted)),
            QuizStarted=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Started),QuizSubmitted=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Submitted),QuizExpired=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Expired),
            QuizPassed=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Submitted&&a.EarnedMarks.HasValue&&a.IsPassed==true),QuizFailed=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Submitted&&a.EarnedMarks.HasValue&&a.IsPassed==false),QuizUnavailable=attempts.Count(a=>a.UserId==e.UserId&&a.Status==QuizAttemptStatus.Submitted&&(!a.EarnedMarks.HasValue||!a.IsPassed.HasValue)),
            Submissions=submissions.Count(s=>s.UserId==e.UserId),Graded=submissions.Count(s=>s.UserId==e.UserId&&source.Grades.Any(g=>g.SubmissionId==s.Id)),Ungraded=submissions.Count(s=>s.UserId==e.UserId&&!source.Grades.Any(g=>g.SubmissionId==s.Id)),
            RecentAssignmentReview=reviewLinks.Where(s=>s.UserId==e.UserId).OrderByDescending(s=>s.SubmittedAt).ThenBy(s=>s.Id).Select(s=>new SubmissionReviewReference(s.CourseId,s.SectionId,s.LessonId,s.AssignmentId,s.Id)).FirstOrDefault()
        });
    }
    internal IQueryable<GradeCourseRow> GradeCourses(ReportFilter filter)
    {
        var courses=Filter(filter,false);var lessons=Lessons(false);var user=org.UserId;
        return from c in courses join e in source.Enrollments.Where(e=>e.UserId==user&&(e.Status==EnrollmentStatus.Active||e.Status==EnrollmentStatus.Completed)) on c.Id equals e.CourseId select new GradeCourseRow{
            CourseId=c.Id,OrganizationId=c.OrganizationId!.Value,UserId=e.UserId,Title=c.Title,Published=c.IsPublished,EnrollmentStatus=(int)e.Status,EnrollmentCompletedAt=e.CompletedAt,StoredEnrollmentPercentage=e.ProgressPercentage,
            PublishedLessons=lessons.Count(l=>l.IsPublished&&source.Sections.Any(s=>s.Id==l.SectionId&&s.CourseId==c.Id)),CompletedPublishedLessons=lessons.Count(l=>l.IsPublished&&source.Sections.Any(s=>s.Id==l.SectionId&&s.CourseId==c.Id)&&source.Progress.Any(p=>p.LessonId==l.Id&&p.UserId==user&&p.IsCompleted))
        };
    }
    internal IQueryable<QuizGradeRow> Quizzes(Guid course,AssessmentFilter filter,DateTime now)
    {
        filter.Validate();var courses=Courses(false).Where(c=>c.Id==course);var lessons=Lessons(false);var user=org.UserId;
        var quizzes=from q in source.Quizzes join l in lessons on q.LessonId equals l.Id join s in source.Sections on l.SectionId equals s.Id join c in courses on s.CourseId equals c.Id select new{q,l,s,c};
        var attempts=from x in quizzes join a in source.Attempts.Where(a=>a.UserId==user) on x.q.Id equals a.QuizId select new QuizGradeRow{
            CourseId=x.c.Id,SectionId=x.s.Id,LessonId=x.l.Id,QuizId=x.q.Id,Title=x.q.Title,AttemptId=a.Id,AttemptNumber=a.AttemptNumber,Status=(int)a.Status,StartedAt=a.StartedAt,SubmittedAt=a.SubmittedAt,Deadline=a.Deadline,PossibleMarks=a.PossibleMarks,PassingPercentageSnapshot=a.PassingPercentageSnapshot,
            EarnedMarks=a.Status==QuizAttemptStatus.Submitted?a.EarnedMarks:null,Passed=a.Status==QuizAttemptStatus.Submitted?a.IsPassed:null,Overdue=a.Status==QuizAttemptStatus.Started&&now>a.Deadline,DetailAvailable=x.c.IsPublished&&x.l.IsPublished&&x.q.IsPublished
        };
        var missing=quizzes.Where(x=>x.c.IsPublished&&x.l.IsPublished&&x.q.IsPublished&&!source.Attempts.Any(a=>a.QuizId==x.q.Id&&a.UserId==user)).Select(x=>new QuizGradeRow{
            CourseId=x.c.Id,SectionId=x.s.Id,LessonId=x.l.Id,QuizId=x.q.Id,Title=x.q.Title,AttemptId=null,AttemptNumber=null,Status=0,StartedAt=null,SubmittedAt=null,Deadline=null,PossibleMarks=null,PassingPercentageSnapshot=null,EarnedMarks=null,Passed=null,Overdue=false,DetailAvailable=true
        });
        var rows=attempts.Concat(missing);if(filter.Status.HasValue)rows=rows.Where(x=>x.Status==filter.Status.Value);return rows;
    }
    internal IQueryable<AssignmentGradeRow> Assignments(Guid course,AssessmentFilter filter)
    {
        filter.Validate();if(filter.Status==3)throw new ArgumentException("Assignment state must be 0–2.");var courses=Courses(false).Where(c=>c.Id==course);var lessons=Lessons(false);var user=org.UserId;
        var work=source.Submissions.Where(s=>s.UserId==user);
        var rows=from a in source.Assignments join l in lessons on a.LessonId equals l.Id join s in source.Sections on l.SectionId equals s.Id join c in courses on s.CourseId equals c.Id
                 join sub in work on a.Id equals sub.AssignmentId into submissions from sub in submissions.DefaultIfEmpty()
                 join grade in source.Grades on (sub==null?Guid.Empty:sub.Id) equals grade.SubmissionId into grades from grade in grades.DefaultIfEmpty()
                 where c.IsPublished&&l.IsPublished&&a.IsPublished||sub!=null
                 select new AssignmentGradeRow{CourseId=c.Id,SectionId=s.Id,LessonId=l.Id,AssignmentId=a.Id,Title=a.Title,SubmissionId=sub==null?null:sub.Id,SubmittedAt=sub==null?null:sub.SubmittedAt,GradedAt=grade==null?null:grade.GradedAt,MaximumMarks=sub==null?a.MaximumMarks:sub.MaximumMarksSnapshot,Marks=grade==null?null:grade.Marks,Feedback=grade==null?null:grade.Feedback,Status=sub==null?0:grade==null?1:2,Published=c.IsPublished&&l.IsPublished&&a.IsPublished};
        if(filter.Status.HasValue)rows=rows.Where(x=>x.Status==filter.Status.Value);return rows;
    }
}
