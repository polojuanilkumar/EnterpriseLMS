using System.ComponentModel.DataAnnotations;
namespace LMS.Web.Models;

public sealed class QuizForm
{
    [Required] public string Title { get; set; } = "";
    public string? Description { get; set; }
    [Range(0,100)] public decimal PassingPercentage { get; set; } = 50;
    [Range(1,int.MaxValue)] public int TimeLimitInMinutes { get; set; } = 15;
    [Range(1,int.MaxValue)] public int MaxAttempts { get; set; } = 1;
}
public sealed record QuizView(Guid Id, Guid LessonId, string Title, string? Description, decimal PassingPercentage, int TimeLimitInMinutes, int MaxAttempts, bool IsPublished);
public sealed class QuestionForm
{
    [Required] public string QuestionText { get; set; } = "";
    [Range(1,3)] public int QuestionType { get; set; } = 1;
    [Range(1,int.MaxValue)] public int DisplayOrder { get; set; } = 1;
    [Range(typeof(decimal),"0.0000000000000000000000000001","79228162514264337593543950335")] public decimal Marks { get; set; } = 1;
}
public sealed class OptionForm
{
    [Required] public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
    [Range(1,int.MaxValue)] public int DisplayOrder { get; set; } = 1;
}
public sealed record QuestionView(Guid Id, Guid QuizId, string QuestionText, int QuestionType, int DisplayOrder, decimal Marks);
public sealed record StaffOptionView(Guid Id, Guid QuestionId, string OptionText, bool IsCorrect, int DisplayOrder);
public sealed record LearnerOptionView(Guid Id, string OptionText, int DisplayOrder);
public sealed record QuizQuestionPage(QuestionView Question, LearnerOptionView[] Options);
public sealed record StaffQuestionPage(QuestionView Question, StaffOptionView[] Options);
public sealed record QuizEditorPage(UnitPage Unit, QuizView? Quiz, StaffQuestionPage[] Questions, QuizForm Form);
public sealed record QuizAttemptView(Guid AttemptId, int AttemptNumber, int Status, DateTime Deadline, decimal? EarnedMarks, decimal PossibleMarks, bool? IsPassed);
public sealed record QuizStartView(Guid AttemptId, DateTime Deadline, int RemainingAttempts);
public sealed record QuizResultView(Guid AttemptId, decimal? EarnedMarks, decimal PossibleMarks, decimal PassingPercentageSnapshot, bool? IsPassed);
public sealed record QuizReviewOption(Guid OptionId, string OptionText);
public sealed record QuizReviewAnswer(Guid QuestionId, string QuestionText, QuizReviewOption[] SelectedOptions);
public sealed record QuizReviewView(Guid AttemptId, QuizReviewAnswer[] Answers);
public sealed record LearnerQuizPage(UnitPage Unit, QuizView? Quiz, QuizAttemptView[] Attempts, bool Active, QuizQuestionPage[] Questions, QuizAttemptView? Attempt = null, QuizResultView? Result = null, QuizReviewView? Review = null, string? Notice = null);
public sealed class QuizSubmission
{
    public List<QuizAnswerForm> Answers { get; set; } = [];
}
public sealed class QuizAnswerForm
{
    public Guid QuestionId { get; set; }
    public List<Guid> SelectedOptionIds { get; set; } = [];
}
