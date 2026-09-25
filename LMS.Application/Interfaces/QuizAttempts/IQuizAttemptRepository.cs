using LMS.Domain.Entities;

namespace LMS.Application.Interfaces.QuizAttempts
{
    public interface IQuizAttemptRepository
    {
        // Serializes starts and submissions for this quiz. Reads and writes share
        // the transaction; returning commits, throwing rolls back.
        Task<T> ExecuteTransactionAsync<T>(Guid quizId,
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

        Task<bool> HasAttemptsAsync(Guid quizId, CancellationToken cancellationToken = default);
        Task<T> ExecuteDeletionTransactionAsync<T>(Guid parentId, bool isSection,
            Func<IReadOnlyList<Guid>, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<QuizAttempt>> GetByQuizAndUserAsync(Guid quizId, Guid userId,
            CancellationToken cancellationToken = default);
        Task<QuizAttempt?> GetByIdAndQuizAndUserAsync(Guid attemptId, Guid quizId, Guid userId,
            CancellationToken cancellationToken = default);
        Task AddAsync(QuizAttempt attempt, CancellationToken cancellationToken = default);
        Task AddAnswersAsync(IEnumerable<QuizAttemptAnswer> answers,
            IEnumerable<QuizAttemptAnswerOption> selectedOptions, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
