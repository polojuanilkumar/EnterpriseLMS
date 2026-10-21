using Microsoft.AspNetCore.WebUtilities;
namespace LMS.Web.Models;
public sealed class ReportFilterView
{
    public int Page{get;set;}=1;public int PageSize{get;set;}=20;public string? Search{get;set;}public bool? Published{get;set;}public int? EnrollmentStatus{get;set;}
    public string Query(int? page=null)=>QueryHelpers.AddQueryString("",new Dictionary<string,string?>{{"page",(page??Page).ToString()},{"pageSize",PageSize.ToString()},{"search",Search},{"published",Published?.ToString().ToLowerInvariant()},{"enrollmentStatus",EnrollmentStatus?.ToString()}});
}
public sealed record ReportPageView<T>(Guid OrganizationId,Guid? OwnerUserId,int Page,int PageSize,int Total,T[] Items)
{public int Pages=>Total==0?0:(int)Math.Ceiling((decimal)Total/PageSize);}
public sealed record ReportCourseReference(Guid CourseId,Guid OrganizationId,Guid InstructorId,string Title,bool Published);
public sealed record ReportCourseView(Guid CourseId,Guid OrganizationId,Guid InstructorId,string Title,bool Published,int Enrollments,int Active,int Completed,int Cancelled,int PublishedLessons,int QuizStarted,int QuizSubmitted,int QuizExpired,int QuizPassed,int QuizFailed,int QuizUnavailable,int Assignments,int Submissions,int Graded,int Ungraded,long ProgressDenominator=0,int CompletedPublishedLearnerLessons=0,decimal CurrentPercentage=0);
public sealed record ReportSubmissionLink(Guid CourseId,Guid SectionId,Guid LessonId,Guid AssignmentId,Guid SubmissionId);
public sealed record ReportLearnerView(Guid CourseId,Guid UserId,string FirstName,string LastName,int EnrollmentStatus,DateTime EnrolledAt,DateTime? EnrollmentCompletedAt,decimal StoredEnrollmentPercentage,int PublishedLessons,int CompletedPublishedLessons,decimal CurrentPercentage,bool CurrentContentComplete,int QuizStarted,int QuizSubmitted,int QuizExpired,int QuizPassed,int QuizFailed,int QuizUnavailable,int Submissions,int Graded,int Ungraded,ReportSubmissionLink? RecentAssignmentReview=null);
public sealed record CourseLearnerReportView(ReportCourseReference Course,ReportPageView<ReportLearnerView> Learners);
public sealed record GradeCourseView(Guid CourseId,Guid OrganizationId,Guid UserId,string Title,bool Published,int EnrollmentStatus,DateTime? EnrollmentCompletedAt,decimal StoredEnrollmentPercentage,int PublishedLessons,int CompletedPublishedLessons,decimal CurrentPercentage);
public sealed record QuizGradeView(Guid CourseId,Guid SectionId,Guid LessonId,Guid QuizId,string Title,Guid? AttemptId,int? AttemptNumber,int Status,DateTime? StartedAt,DateTime? SubmittedAt,DateTime? Deadline,decimal? EarnedMarks,decimal? PossibleMarks,decimal? PassingPercentageSnapshot,bool? Passed,bool Overdue,bool DetailAvailable,bool ResultAvailable);
public sealed record AssignmentGradebookView(Guid CourseId,Guid SectionId,Guid LessonId,Guid AssignmentId,string Title,Guid? SubmissionId,DateTime? SubmittedAt,DateTime? GradedAt,decimal MaximumMarks,decimal? Marks,string? Feedback,int Status,bool Published);
public sealed record StaffReportsPage(Guid OrganizationId,ReportFilterView Filter,ReportPageView<ReportCourseView> Courses);
public sealed record StaffLearnersReportPage(Guid OrganizationId,ReportFilterView Filter,CourseLearnerReportView Report);
public sealed record MyGradesPage(Guid OrganizationId,ReportFilterView Filter,ReportPageView<GradeCourseView> Courses);
public sealed record CourseGradesPage(Guid OrganizationId,GradeCourseView Course,ReportPageView<QuizGradeView> Quizzes,ReportPageView<AssignmentGradebookView> Assignments,int? QuizStatus,int? AssignmentStatus)
{
    public string Link(int quizPage,int assignmentPage)=>QueryHelpers.AddQueryString($"/learn/{OrganizationId}/grades/courses/{Course.CourseId}",new Dictionary<string,string?>{{"quizPage",quizPage.ToString()},{"assignmentPage",assignmentPage.ToString()},{"pageSize",Quizzes.PageSize.ToString()},{"quizStatus",QuizStatus?.ToString()},{"assignmentStatus",AssignmentStatus?.ToString()}});
}
public static class ReportText
{
    public static string Enrollment(int status)=>status switch{1=>"Active",2=>"Completed",3=>"Cancelled",_=>"Unavailable"};
    public static string Quiz(QuizGradeView q)=>q.Status switch{0=>"Not attempted",1=>q.Overdue?"In progress — deadline passed (stored status Started)":"In progress",2=>q.ResultAvailable?"Submitted":"Submitted — result unavailable",3=>"Expired",_=>"Unavailable"};
    public static string Assignment(int status)=>status switch{0=>"Not submitted",1=>"Submitted / ungraded",2=>"Graded",_=>"Unavailable"};
    public static string Utc(DateTime? value)=>value?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")??"Not recorded";
}
