using LMS.Application.Interfaces.QuizAttempts;
using LMS.Domain.Enums;

namespace LMS.Application.Features.QuizAttempts.GetQuizAttemptAnswers
{
    public sealed class GetQuizAttemptAnswersHandler
    {
        private readonly IQuizAttemptRepository _attempts;

        public GetQuizAttemptAnswersHandler(IQuizAttemptRepository attempts) => _attempts = attempts;

        public async Task<GetQuizAttemptAnswersResponse> HandleAsync(Guid quizId, Guid attemptId,
            Guid userId, CancellationToken cancellationToken = default)
        {
            var attempt = await _attempts.GetByIdAndQuizAndUserAsync(
                attemptId, quizId, userId, cancellationToken)
                ?? throw new KeyNotFoundException("Quiz attempt not found.");

            if (attempt.Status != QuizAttemptStatus.Submitted)
                throw new InvalidOperationException("Answers are only available for submitted quiz attempts.");

            var answers = await _attempts.GetAnswersAsync(attempt.Id, cancellationToken);
            var selectedOptions = await _attempts.GetSelectedOptionsAsync(attempt.Id, cancellationToken);
            var optionsByAnswer = selectedOptions.ToLookup(x => x.AnswerId);

            return new GetQuizAttemptAnswersResponse
            {
                AttemptId = attempt.Id,
                // Snapshots do not store DisplayOrder. Use stable IDs, not the live question order.
                Answers = answers.OrderBy(x => x.QuestionId).Select(answer => new QuizAttemptAnswerResponse
                {
                    QuestionId = answer.QuestionId,
                    QuestionText = answer.QuestionTextSnapshot,
                    QuestionType = answer.QuestionTypeSnapshot,
                    Marks = answer.MarksSnapshot,
                    SelectedOptions = optionsByAnswer[answer.Id].OrderBy(x => x.OptionId)
                        .Select(option => new QuizAttemptSelectedOptionResponse
                        {
                            OptionId = option.OptionId,
                            OptionText = option.OptionTextSnapshot
                        }).ToList()
                }).ToList()
            };
        }
    }
}
