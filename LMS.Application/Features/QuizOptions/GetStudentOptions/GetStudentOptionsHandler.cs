using LMS.Application.Interfaces.QuizOptions;
using LMS.Application.Interfaces.QuizQuestions;
using LMS.Application.Interfaces.Quizzes;

namespace LMS.Application.Features.QuizOptions.GetStudentOptions
{
    public sealed class GetStudentOptionsHandler
    {
        private readonly IQuizOptionRepository _optionRepository;
        private readonly IQuizQuestionRepository _questionRepository;
        private readonly IQuizRepository _quizRepository;

        public GetStudentOptionsHandler(
            IQuizOptionRepository optionRepository,
            IQuizQuestionRepository questionRepository,
            IQuizRepository quizRepository)
        {
            _optionRepository = optionRepository;
            _questionRepository = questionRepository;
            _quizRepository = quizRepository;
        }

        public async Task<IReadOnlyList<StudentQuizOptionResponse>> HandleAsync(
            Guid quizId,
            Guid questionId,
            CancellationToken cancellationToken = default)
        {
            var quiz = await _quizRepository.GetByIdAsync(quizId, cancellationToken);

            if (quiz is null || !quiz.IsPublished)
                throw new KeyNotFoundException("Quiz not found.");

            var question = await _questionRepository.GetByIdAsync(questionId, cancellationToken);

            if (question is null || question.QuizId != quizId)
                throw new KeyNotFoundException("Question not found.");

            var options = await _optionRepository.GetByQuestionIdAsync(questionId, cancellationToken);

            return options.Select(x => new StudentQuizOptionResponse
            {
                Id = x.Id,
                OptionText = x.OptionText,
                DisplayOrder = x.DisplayOrder
            }).ToList();
        }
    }

    public sealed class StudentQuizOptionResponse
    {
        public Guid Id { get; init; }
        public string OptionText { get; init; } = null!;
        public int DisplayOrder { get; init; }
    }
}
