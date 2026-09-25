namespace LMS.Application.Features.QuizAttempts.SubmitQuizAttempt
{
    public sealed class SubmitQuizAttemptRequest
    {
        public List<SubmitQuizAnswerRequest> Answers { get; init; } = [];
    }

    public sealed class SubmitQuizAnswerRequest
    {
        public Guid QuestionId { get; init; }
        public List<Guid> SelectedOptionIds { get; init; } = [];
    }
}
