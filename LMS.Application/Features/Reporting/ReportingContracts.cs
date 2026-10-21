namespace LMS.Application.Features.Reporting;

public sealed record ReportFilter(int Page=1,int PageSize=20,string? Search=null,bool? Published=null,int? EnrollmentStatus=null)
{
    public int Skip=>(Page-1)*PageSize;
    public void Validate(bool learner=false)
    {
        if(Page<1||Page>10000||PageSize<1||PageSize>50||Search?.Length>100||EnrollmentStatus is {} s&&(s<1||s>(learner?2:3)))throw new ArgumentException("Page must be 1–10000, page size 1–50, search at most 100 characters, and enrollment state must be supported.");
    }
}
public sealed record AssessmentFilter(int Page=1,int PageSize=20,int? Status=null)
{
    public int Skip=>(Page-1)*PageSize;
    public void Validate(){if(Page<1||Page>10000||PageSize<1||PageSize>50||Status is <0 or >3)throw new ArgumentException("Page must be 1–10000, page size 1–50, assessment state 0–3.");}
}
public sealed record ReportPage<T>(Guid OrganizationId,Guid? OwnerUserId,int Page,int PageSize,int Total,T[] Items)
{public int Pages=>Total==0?0:(int)Math.Ceiling((decimal)Total/PageSize);}
public sealed record CourseReference(Guid CourseId,Guid OrganizationId,Guid InstructorId,string Title,bool Published);
public sealed record SubmissionReviewReference(Guid CourseId,Guid SectionId,Guid LessonId,Guid AssignmentId,Guid SubmissionId);
public sealed record CourseReportRow
{
    public Guid CourseId{get;init;}public Guid OrganizationId{get;init;}public Guid InstructorId{get;init;}public string Title{get;init;}="";public bool Published{get;init;}
    public int Enrollments{get;init;}public int Active{get;init;}public int Completed{get;init;}public int Cancelled{get;init;}public int PublishedLessons{get;init;}
    public int QuizStarted{get;init;}public int QuizSubmitted{get;init;}public int QuizExpired{get;init;}public int QuizPassed{get;init;}public int QuizFailed{get;init;}public int QuizUnavailable{get;init;}
    public int Assignments{get;init;}public int Submissions{get;init;}public int Graded{get;init;}public int Ungraded{get;init;}
    public int CompletedPublishedLearnerLessons{get;init;}
    public long ProgressDenominator=>(long)Enrollments*PublishedLessons;
    public decimal CurrentPercentage=>ProgressDenominator==0?0:Math.Round(100m*CompletedPublishedLearnerLessons/ProgressDenominator,2);
}
public sealed record LearnerReportRow
{
    public Guid CourseId{get;init;}public Guid UserId{get;init;}public string FirstName{get;init;}="";public string LastName{get;init;}="";
    public int EnrollmentStatus{get;init;}public DateTime EnrolledAt{get;init;}public DateTime? EnrollmentCompletedAt{get;init;}public decimal StoredEnrollmentPercentage{get;init;}
    public int PublishedLessons{get;init;}public int CompletedPublishedLessons{get;init;}
    public decimal CurrentPercentage=>PublishedLessons==0?0:Math.Round(100m*CompletedPublishedLessons/PublishedLessons,2);
    public bool CurrentContentComplete=>PublishedLessons>0&&CompletedPublishedLessons==PublishedLessons;
    public int QuizStarted{get;init;}public int QuizSubmitted{get;init;}public int QuizExpired{get;init;}public int QuizPassed{get;init;}public int QuizFailed{get;init;}public int QuizUnavailable{get;init;}
    public int Submissions{get;init;}public int Graded{get;init;}public int Ungraded{get;init;}
    public SubmissionReviewReference? RecentAssignmentReview{get;init;}
}
public sealed record CourseLearnerReport(CourseReference Course,ReportPage<LearnerReportRow> Learners);
public sealed record GradeCourseRow
{
    public Guid CourseId{get;init;}public Guid OrganizationId{get;init;}public Guid UserId{get;init;}public string Title{get;init;}="";public bool Published{get;init;}
    public int EnrollmentStatus{get;init;}public DateTime? EnrollmentCompletedAt{get;init;}public decimal StoredEnrollmentPercentage{get;init;}
    public int PublishedLessons{get;init;}public int CompletedPublishedLessons{get;init;}
    public decimal CurrentPercentage=>PublishedLessons==0?0:Math.Round(100m*CompletedPublishedLessons/PublishedLessons,2);
}
public sealed record QuizGradeRow
{
    public Guid CourseId{get;init;}public Guid SectionId{get;init;}public Guid LessonId{get;init;}public Guid QuizId{get;init;}public string Title{get;init;}="";
    public Guid? AttemptId{get;init;}public int? AttemptNumber{get;init;}public int Status{get;init;}public DateTime? StartedAt{get;init;}public DateTime? SubmittedAt{get;init;}public DateTime? Deadline{get;init;}
    public decimal? EarnedMarks{get;init;}public decimal? PossibleMarks{get;init;}public decimal? PassingPercentageSnapshot{get;init;}public bool? Passed{get;init;}public bool Overdue{get;init;}public bool DetailAvailable{get;init;}
    public bool ResultAvailable=>Status==2&&EarnedMarks.HasValue&&Passed.HasValue;
}
public sealed record AssignmentGradeRow
{
    public Guid CourseId{get;init;}public Guid SectionId{get;init;}public Guid LessonId{get;init;}public Guid AssignmentId{get;init;}public string Title{get;init;}="";
    public Guid? SubmissionId{get;init;}public DateTime? SubmittedAt{get;init;}public DateTime? GradedAt{get;init;}public decimal MaximumMarks{get;init;}public decimal? Marks{get;init;}public string? Feedback{get;init;}
    // 0 not submitted, 1 submitted/ungraded, 2 graded (including zero).
    public int Status{get;init;}public bool Published{get;init;}
}
public interface IReportingRepository
{
    Task<ReportPage<CourseReportRow>> Courses(ReportFilter filter,CancellationToken token);
    Task<CourseReference?> StaffCourse(Guid id,CancellationToken token);
    Task<ReportPage<LearnerReportRow>> Learners(Guid course,ReportFilter filter,CancellationToken token);
    Task<ReportPage<GradeCourseRow>> GradeCourses(ReportFilter filter,CancellationToken token);
    Task<GradeCourseRow?> GradeCourse(Guid course,CancellationToken token);
    Task<ReportPage<QuizGradeRow>> Quizzes(Guid course,AssessmentFilter filter,DateTime now,CancellationToken token);
    Task<ReportPage<AssignmentGradeRow>> Assignments(Guid course,AssessmentFilter filter,CancellationToken token);
}
