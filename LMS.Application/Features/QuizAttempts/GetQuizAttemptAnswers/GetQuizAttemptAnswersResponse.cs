using LMS.Domain.Entities;

namespace LMS.Application.Features.QuizAttempts.GetQuizAttemptAnswers
{
    public sealed class GetQuizAttemptAnswersResponse
    {
        public Guid AttemptId { get; init; }
        public IReadOnlyList<QuizAttemptAnswerResponse> Answers { get; init; } = [];
    }

    public sealed class QuizAttemptAnswerResponse
    {
        public Guid QuestionId { get; init; }
        public string QuestionText { get; init; } = string.Empty;
        public QuestionType QuestionType { get; init; }
        public decimal Marks { get; init; }
        public IReadOnlyList<QuizAttemptSelectedOptionResponse> SelectedOptions { get; init; } = [];
    }

    public sealed class QuizAttemptSelectedOptionResponse
    {
        public Guid OptionId { get; init; }
        public string OptionText { get; init; } = string.Empty;
    }
}
