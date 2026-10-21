using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace LMS.Web.Models;

public sealed record BffQuizQuestion(Guid Id, string QuestionText, int QuestionType, int DisplayOrder, decimal Marks, LearnerOptionView[] Options);
public sealed record BffStudentQuiz(Guid OrganizationId, Guid CourseId, Guid SectionId, Guid LessonId, QuizView? Quiz,
    bool Active, QuizAttemptView[] Attempts, QuizAttemptView? Attempt, BffQuizQuestion[] Questions, QuizResultView? Result, QuizReviewView? Review);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffQuizStart;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffQuizAnswer(Guid QuestionId, [Required] List<Guid> SelectedOptionIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BffQuizSubmit(bool Confirmed, [Required] List<BffQuizAnswer> Answers);
