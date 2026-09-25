using System.Data;
using LMS.Application.Interfaces.QuizAttempts;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LMS.Infrastructure.Repositories
{
    public sealed class QuizAttemptRepository : IQuizAttemptRepository
    {
        private readonly LMSDbContext _context;

        public QuizAttemptRepository(LMSDbContext context) => _context = context;

        public async Task<T> ExecuteTransactionAsync<T>(Guid quizId,
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                // Lock an existing parent even when the student has no attempts yet.
                // UPDLOCK avoids concurrent readers both trying to upgrade to writers.
                // Serializes starts and submissions across students for the same quiz.
                await _context.Quizzes.FromSqlInterpolated(
                    $"SELECT * FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {quizId}")
                    .AsNoTracking().ToListAsync(cancellationToken);

                var result = await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                throw new InvalidOperationException(
                    "A conflicting quiz attempt operation occurred. Refresh and try again.", exception);
            }
        }

        private static bool IsConcurrencyConflict(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException sql && sql.Errors.Cast<SqlError>()
                    .Any(error => error.Number is 2601 or 2627 or 1205))
                    return true;
            }
            return false;
        }

        public Task<bool> HasAttemptsAsync(Guid quizId, CancellationToken cancellationToken = default)
            => _context.QuizAttempts.AnyAsync(x => x.QuizId == quizId, cancellationToken);

        public async Task<T> ExecuteDeletionTransactionAsync<T>(Guid parentId, bool isSection,
            Func<IReadOnlyList<Guid>, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                // Protect parent/descendant ranges, including when no quiz exists.
                // Quiz update locks coordinate with Start/Submit and direct mutations.
                List<Quiz> quizzes;
                if (isSection)
                {
                    await _context.CourseSections.FromSqlInterpolated(
                        $"SELECT * FROM [CourseSections] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {parentId}")
                        .AsNoTracking().ToListAsync(cancellationToken);
                    quizzes = await _context.Quizzes.FromSqlInterpolated(
                        $"SELECT q.* FROM [Quizzes] q WITH (UPDLOCK, HOLDLOCK) WHERE q.[LessonId] IN (SELECT [Id] FROM [Lessons] WITH (HOLDLOCK) WHERE [SectionId] = {parentId}) ORDER BY q.[Id]")
                        .AsNoTracking().ToListAsync(cancellationToken);
                }
                else
                {
                    await _context.Lessons.FromSqlInterpolated(
                        $"SELECT * FROM [Lessons] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {parentId}")
                        .AsNoTracking().ToListAsync(cancellationToken);
                    quizzes = await _context.Quizzes.FromSqlInterpolated(
                        $"SELECT * FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [LessonId] = {parentId} ORDER BY [Id]")
                        .AsNoTracking().ToListAsync(cancellationToken);
                }
                var result = await action(quizzes.Select(x => x.Id).ToList(), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                throw new InvalidOperationException(
                    "A conflicting quiz attempt operation occurred. Refresh and try again.", exception);
            }
        }

        public async Task<IReadOnlyList<QuizAttempt>> GetByQuizAndUserAsync(Guid quizId,
            Guid userId, CancellationToken cancellationToken = default)
            => await _context.QuizAttempts.Where(x => x.QuizId == quizId && x.UserId == userId)
                .ToListAsync(cancellationToken);

        public async Task AddAsync(QuizAttempt attempt, CancellationToken cancellationToken = default)
            => await _context.QuizAttempts.AddAsync(attempt, cancellationToken);

        public Task<QuizAttempt?> GetByIdAndQuizAndUserAsync(Guid attemptId, Guid quizId,
            Guid userId, CancellationToken cancellationToken = default)
            => _context.QuizAttempts.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == attemptId && x.QuizId == quizId && x.UserId == userId,
                cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => await _context.SaveChangesAsync(cancellationToken);

        public async Task AddAnswersAsync(IEnumerable<QuizAttemptAnswer> answers,
            IEnumerable<QuizAttemptAnswerOption> selectedOptions, CancellationToken cancellationToken = default)
        {
            await _context.QuizAttemptAnswers.AddRangeAsync(answers, cancellationToken);
            await _context.QuizAttemptAnswerOptions.AddRangeAsync(selectedOptions, cancellationToken);
        }
    }
}
