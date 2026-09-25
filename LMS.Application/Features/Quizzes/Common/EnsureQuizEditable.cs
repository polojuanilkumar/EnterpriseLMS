using LMS.Application.Interfaces.Quizzes;
using LMS.Application.Interfaces.QuizAttempts;

namespace LMS.Application.Features.Quizzes.Common
{
    public sealed class EnsureQuizEditable
    {
        private readonly IQuizRepository _quizRepository;
        private readonly IQuizAttemptRepository _attemptRepository;

        public EnsureQuizEditable(IQuizRepository quizRepository, IQuizAttemptRepository attemptRepository)
        {
            _quizRepository = quizRepository;
            _attemptRepository = attemptRepository;
        }

        public Task<T> ExecuteAsync<T>(Guid quizId, Func<CancellationToken, Task<T>> mutation,
            CancellationToken cancellationToken = default)
            => _attemptRepository.ExecuteTransactionAsync(quizId, mutation, cancellationToken);

        public Task ExecuteAsync(Guid quizId, Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default)
            => ExecuteAsync(quizId, async token => { await mutation(token); return true; }, cancellationToken);

        public async Task<T> ExecuteForLessonAsync<T>(Guid lessonId, Func<CancellationToken, Task<T>> mutation,
            CancellationToken cancellationToken = default)
        {
            // Resolve only the ID before locking, avoiding stale tracked quiz settings.
            var quizId = await _quizRepository.GetIdByLessonIdAsync(lessonId, cancellationToken)
                ?? throw new KeyNotFoundException("Quiz not found.");
            return await ExecuteAsync(quizId, async token =>
            {
                // A no-attempt quiz could have been deleted/recreated between ID
                // resolution and lock acquisition. Never mutate a different quiz.
                if (await _quizRepository.GetIdByLessonIdAsync(lessonId, token) != quizId)
                    throw new InvalidOperationException("The lesson's quiz changed. Refresh and try again.");
                return await mutation(token);
            }, cancellationToken);
        }

        public Task ExecuteForLessonAsync(Guid lessonId, Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default)
            => ExecuteForLessonAsync(lessonId, async token => { await mutation(token); return true; }, cancellationToken);

        public Task ExecuteParentDeletionAsync(Guid parentId, bool isSection,
            Func<CancellationToken, Task> mutation, CancellationToken cancellationToken = default)
            => _attemptRepository.ExecuteDeletionTransactionAsync(parentId, isSection, async (quizIds, token) =>
            {
                foreach (var quizId in quizIds)
                    await CheckNoAttemptsAsync(quizId, token);
                await mutation(token);
                return true;
            }, cancellationToken);

        // Call checks inside one of the transaction wrappers, together with the mutation/save.
        public async Task CheckNoAttemptsAsync(Guid quizId, CancellationToken cancellationToken = default)
        {
            if (await _attemptRepository.HasAttemptsAsync(quizId, cancellationToken))
                throw new InvalidOperationException(
                    "Quiz questions, options, passing percentage, time limit and deletion are locked once an attempt exists.");
        }

        public async Task CheckAsync(Guid quizId, CancellationToken cancellationToken = default,
            bool allowExistingAttempts = false)
        {
            var quiz = await _quizRepository.GetByIdAsync(quizId, cancellationToken)
                ?? throw new KeyNotFoundException("Quiz not found.");
            if (quiz.IsPublished)
                throw new InvalidOperationException(
                    "Unpublish the quiz before editing or deleting it or changing its questions or options.");
            if (!allowExistingAttempts)
                await CheckNoAttemptsAsync(quizId, cancellationToken);
        }
    }
}
