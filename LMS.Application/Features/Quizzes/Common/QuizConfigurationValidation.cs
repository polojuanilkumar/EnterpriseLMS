using LMS.Domain.Entities;

namespace LMS.Application.Features.Quizzes.Common
{
    internal static class QuizConfigurationValidation
    {
        public static string? GetQuestionsError(IReadOnlyList<QuizQuestion> questions)
        {
            if (questions.Count == 0)
                return "Add at least one question before publishing or attempting the quiz.";
            if (questions.Any(x => x.Marks <= 0))
                return "Every quiz question must have positive marks.";
            return null;
        }

        public static string? GetQuestionError(QuizQuestion question, IReadOnlyList<QuizOption> options)
        {
            if (options.Count < 2)
                return $"Question {question.DisplayOrder} needs at least two options.";

            var correctCount = options.Count(x => x.IsCorrect);
            switch (question.QuestionType)
            {
                case QuestionType.SingleChoice:
                    if (correctCount != 1)
                        return $"Question {question.DisplayOrder} must have exactly one correct option.";
                    break;
                case QuestionType.MultipleChoice:
                    if (correctCount < 2)
                        return $"Question {question.DisplayOrder} needs at least two correct options.";
                    break;
                case QuestionType.TrueFalse:
                    if (options.Count != 2 || correctCount != 1)
                        return $"True/False question {question.DisplayOrder} needs exactly two options and one correct option.";
                    break;
                default:
                    return $"Question {question.DisplayOrder} has an unsupported question type.";
            }
            return null;
        }
    }
}
