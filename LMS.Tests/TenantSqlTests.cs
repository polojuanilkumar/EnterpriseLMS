using System.Data.Common;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LMS.Tests;

public sealed class TenantSqlTests
{
    internal static LMSDbContext CreateContext(OrganizationContext? organization = null, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<LMSDbContext>()
            // Offline ADO stub plus an interceptor prohibit opening ANY connection.
            .UseSqlServer(new OfflineConnection())
            .AddInterceptors(new NeverConnect()).AddInterceptors(interceptors).Options, organization);

    internal static OrganizationContext Scope(Guid organization, OrganizationRole role = OrganizationRole.Student, bool platform = false)
    {
        var context = new OrganizationContext();
        context.Initialize(organization, Guid.NewGuid(), role, platform);
        return context;
    }

    private sealed class NeverConnect : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => throw new InvalidOperationException("Tests must never open a database connection.");
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Tests must never open a database connection.");
    }

    // ADO stub permits command construction only. It has no server, socket, or working
    // execution path; CaptureSql throws before a SqlCommand can execute.
    private sealed class OfflineConnection : DbConnection
    {
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "OfflineOnly";
        public override string DataSource => "NoServer";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() => throw new InvalidOperationException("No connections allowed.");
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new SqlCommand();
    }

    private sealed class CapturedSqlException : Exception;
    private sealed class CaptureSql : DbCommandInterceptor
    {
        public string Sql { get; private set; } = "";
        public Dictionary<string, object?> Parameters { get; } = new();
        public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
        {
            Sql = result.CommandText;
            foreach (DbParameter parameter in result.Parameters) Parameters[parameter.ParameterName] = parameter.Value;
            throw new CapturedSqlException(); // Command built but never executed; no physical connection exists.
        }
    }

    private static IQueryable Query(LMSDbContext db, string entity) => entity switch
    {
        "course" => db.Courses, "section" => db.CourseSections, "lesson" => db.Lessons,
        "quiz" => db.Quizzes, "question" => db.QuizQuestions, "option" => db.QuizOptions,
        "enrollment" => db.CourseEnrollments, "progress" => db.LessonProgresses,
        "attempt" => db.QuizAttempts, "answer" => db.QuizAttemptAnswers,
        "selection" => db.QuizAttemptAnswerOptions, _ => throw new ArgumentException(entity)
    };

    [Theory]
    [InlineData("course")][InlineData("section")][InlineData("lesson")][InlineData("quiz")]
    [InlineData("question")][InlineData("option")][InlineData("enrollment")][InlineData("progress")]
    [InlineData("attempt")][InlineData("answer")][InlineData("selection")]
    public void Every_learning_query_resolves_stored_course_organization_with_request_specific_parameter(string entity)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var first = CreateContext(Scope(a));
        using var second = CreateContext(Scope(b, OrganizationRole.Admin, true));
        var sqlA = Query(first, entity).ToQueryString();
        var sqlB = Query(second, entity).ToQueryString();
        Assert.Contains("[Courses]", sqlA);
        Assert.Contains("[OrganizationId] IS NOT NULL", sqlA);
        Assert.Contains("[OrganizationId] = @", sqlA);
        Assert.Contains(a.ToString(), sqlA);
        Assert.DoesNotContain(b.ToString(), sqlA);
        Assert.Contains(b.ToString(), sqlB); // Same cached EF model must not retain organization A.
        Assert.DoesNotContain(a.ToString(), sqlB);
    }

    [Fact]
    public void Missing_context_does_not_disable_the_tenant_filter()
    {
        using var db = CreateContext();
        var sql = db.Courses.ToQueryString();
        Assert.Contains("[OrganizationId] IS NOT NULL", sql);
        Assert.Contains(Guid.Empty.ToString(), sql);
    }

    private static Task RepositoryRead(LMSDbContext db, string operation, Guid id, Guid user) => operation switch
    {
        "course-list" => new CourseRepository(db).GetAllAsync(),
        "course-detail" => new CourseRepository(db).GetByIdAsync(id),
        "course-code" => new CourseRepository(db).ExistsByCodeAsync("CODE"),
        "section-list" => new CourseSectionRepository(db).GetByCourseIdAsync(id),
        "section-detail" => new CourseSectionRepository(db).GetByIdAsync(id),
        "lesson-list" => new LessonRepository(db).GetBySectionIdAsync(id),
        "course-lessons" => new LessonRepository(db).GetByCourseIdAsync(id),
        "lesson-detail" => new LessonRepository(db).GetByIdAsync(id),
        "quiz-detail" => new QuizRepository(db).GetByIdAsync(id),
        "lesson-quiz" => new QuizRepository(db).GetByLessonIdAsync(id),
        "quiz-id" => new QuizRepository(db).GetIdByLessonIdAsync(id),
        "question-list" => new QuizQuestionRepository(db).GetByQuizIdAsync(id),
        "question-detail" => new QuizQuestionRepository(db).GetByIdAsync(id),
        "option-list" => new QuizOptionRepository(db).GetByQuestionIdAsync(id),
        "option-detail" => new QuizOptionRepository(db).GetByIdAsync(id),
        "enrollment-list" => new CourseEnrollmentRepository(db).GetByUserIdAsync(user),
        "enrollment-detail" => new CourseEnrollmentRepository(db).GetByIdAsync(id),
        "enrollment-pair" => new CourseEnrollmentRepository(db).GetByCourseAndUserAsync(id, user),
        "progress-detail" => new LessonProgressRepository(db).GetByUserAndLessonAsync(user, id),
        "progress-list" => new LessonProgressRepository(db).GetByUserAndLessonIdsAsync(user, new[] { id }),
        "attempt-list" => new QuizAttemptRepository(db).GetByQuizAndUserAsync(id, user),
        "attempt-detail" => new QuizAttemptRepository(db).GetByIdAndQuizAndUserAsync(id, id, user),
        "attempt-exists" => new QuizAttemptRepository(db).HasAttemptsAsync(id),
        "answers" => new QuizAttemptRepository(db).GetAnswersAsync(id),
        "selections" => new QuizAttemptRepository(db).GetSelectedOptionsAsync(id),
        "completion-precheck" => new CourseEnrollmentRepository(db).ExecuteEnrollmentTransactionAsync(id, _ => throw new Exception("Must not reach callback")),
        "attempt-precheck" => new QuizAttemptRepository(db).ExecuteTransactionAsync(id, _ => Task.FromResult(true)),
        "section-delete-precheck" => new QuizAttemptRepository(db).ExecuteDeletionTransactionAsync(id, true, (_, _) => Task.FromResult(true)),
        "lesson-delete-precheck" => new QuizAttemptRepository(db).ExecuteDeletionTransactionAsync(id, false, (_, _) => Task.FromResult(true)),
        _ => throw new ArgumentException(operation)
    };

    [Theory]
    [InlineData("course-list")][InlineData("course-detail")][InlineData("course-code")]
    [InlineData("section-list")][InlineData("section-detail")][InlineData("lesson-list")]
    [InlineData("course-lessons")][InlineData("lesson-detail")][InlineData("quiz-detail")]
    [InlineData("lesson-quiz")][InlineData("quiz-id")][InlineData("question-list")]
    [InlineData("question-detail")][InlineData("option-list")][InlineData("option-detail")]
    [InlineData("enrollment-list")][InlineData("enrollment-detail")][InlineData("enrollment-pair")]
    [InlineData("progress-detail")][InlineData("progress-list")][InlineData("attempt-list")]
    [InlineData("attempt-detail")][InlineData("attempt-exists")][InlineData("answers")][InlineData("selections")]
    [InlineData("completion-precheck")][InlineData("attempt-precheck")]
    [InlineData("section-delete-precheck")][InlineData("lesson-delete-precheck")]
    public async Task Actual_repository_SQL_includes_tenant_filter_before_any_connection_or_transaction(string operation)
    {
        var organization = Guid.NewGuid(); var user = Guid.NewGuid(); var id = Guid.NewGuid();
        var capture = new CaptureSql();
        using var db = CreateContext(Scope(organization), capture);
        await Assert.ThrowsAsync<CapturedSqlException>(() => RepositoryRead(db, operation, id, user));
        Assert.Contains("[Courses]", capture.Sql);
        Assert.Contains("[OrganizationId] IS NOT NULL", capture.Sql);
        Assert.Contains("[OrganizationId] = @", capture.Sql);
        Assert.Contains(organization, capture.Parameters.Values);
        if (operation is "enrollment-list" or "enrollment-pair" or "progress-detail" or "progress-list" or "attempt-list" or "attempt-detail")
        {
            Assert.Contains("[UserId] = @", capture.Sql);
            Assert.Contains(user, capture.Parameters.Values);
        }
    }

    [Fact]
    public async Task Instructor_course_list_and_detail_filter_assigned_owner_in_SQL()
    {
        var organization=Guid.NewGuid();var scope=Scope(organization,OrganizationRole.Instructor);
        foreach(var operation in new[]{"course-list","course-detail"})
        {
            var capture=new CaptureSql();using var db=CreateContext(scope,capture);
            await Assert.ThrowsAsync<CapturedSqlException>(()=>RepositoryRead(db,operation,Guid.NewGuid(),scope.UserId));
            Assert.Contains("[InstructorId] = @",capture.Sql);
            Assert.Contains(scope.UserId,capture.Parameters.Values);
            Assert.Contains(organization,capture.Parameters.Values);
        }
    }

    [Theory]
    [InlineData("enrollment")][InlineData("quiz")][InlineData("section")][InlineData("lesson")]
    [InlineData("section-quizzes")][InlineData("lesson-quizzes")]
    public void Raw_lock_queries_remain_composable_and_tenant_filtered(string entity)
    {
        var organization = Guid.NewGuid(); var id = Guid.NewGuid();
        using var db = CreateContext(Scope(organization));
        IQueryable query = entity switch
        {
            "enrollment" => db.CourseEnrollments.FromSqlInterpolated($"SELECT * FROM [CourseEnrollments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}"),
            "quiz" => db.Quizzes.FromSqlInterpolated($"SELECT * FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}"),
            "section" => db.CourseSections.FromSqlInterpolated($"SELECT * FROM [CourseSections] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}"),
            "section-quizzes" => db.Quizzes.FromSqlInterpolated($"SELECT q.* FROM [Quizzes] q WITH (UPDLOCK, HOLDLOCK) WHERE q.[LessonId] IN (SELECT [Id] FROM [Lessons] WITH (HOLDLOCK) WHERE [SectionId] = {id})").OrderBy(x => x.Id),
            "lesson-quizzes" => db.Quizzes.FromSqlInterpolated($"SELECT * FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [LessonId] = {id}").OrderBy(x => x.Id),
            _ => db.Lessons.FromSqlInterpolated($"SELECT * FROM [Lessons] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
        };
        var sql = query.ToQueryString();
        Assert.Contains("UPDLOCK, HOLDLOCK", sql);
        Assert.Contains("[OrganizationId] = @", sql);
        Assert.Contains(organization.ToString(), sql);
    }
}
