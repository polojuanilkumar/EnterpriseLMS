using System.Linq.Expressions;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LMS.Tests;

// Evaluates the actual model predicates over two organization graphs, without an EF
// in-memory provider. SQL translation is tested separately by TenantSqlTests.
internal sealed class TenantGraph : IDisposable
{
    public Guid A { get; } = Guid.NewGuid();
    public Guid B { get; } = Guid.NewGuid();
    public LearningFlowFixture First { get; } = new();
    public LearningFlowFixture Second { get; } = new();
    private readonly Dictionary<Type, List<object>> rows = new();
    private readonly LMSDbContext db;
    public Guid Selected { get; set; }

    public TenantGraph(bool platform = false)
    {
        Selected = A;
        db = TenantSqlTests.CreateContext(TenantSqlTests.Scope(A, OrganizationRole.Admin, platform));
        First.Course.AssignOrganization(A);
        Second.Course.AssignOrganization(B);
        AddGraph(First); AddGraph(Second); AddGraph(new LearningFlowFixture()); // legacy null owner
    }

    private void AddGraph(LearningFlowFixture f)
    {
        var question = new QuizQuestion(f.Quiz.Id, "Question", QuestionType.SingleChoice, 1, 1);
        var option = new QuizOption(question.Id, "Option", true, 1);
        var attempt = new QuizAttempt(f.Quiz.Id, f.UserId, 1, DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(10), 1, 70);
        var answer = new QuizAttemptAnswer(attempt.Id, question.Id, "Question", QuestionType.SingleChoice, 1, 1, true);
        foreach (var row in new object[] { f.Course, f.Section, f.Lesson, f.Quiz, f.Enrollment,
            f.Progress, new LessonContentItem(f.Lesson.Id, LessonContentType.Text, "Text", 0, true, "Body", null), question, option, attempt, answer, new QuizAttemptAnswerOption(answer.Id, option.Id, "Option") })
        {
            if (!rows.TryGetValue(row.GetType(), out var list)) rows[row.GetType()] = list = new();
            list.Add(row);
        }
    }

    public IQueryable<T> Visible<T>() where T : class
    {
        var expression = db.Model.FindEntityType(typeof(T))!.GetDeclaredQueryFilters().Single().Expression;
        var predicate = (Expression<Func<T, bool>>)new Substitute(this).Visit(expression)!;
        return rows[typeof(T)].Cast<T>().Where(predicate.Compile()).AsQueryable();
    }

    private sealed class Substitute(TenantGraph graph) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression root)
            {
                var element = root.EntityType.ClrType;
                var query = typeof(TenantGraph).GetMethod(nameof(Visible))!.MakeGenericMethod(element).Invoke(graph, null);
                return Expression.Constant(query, typeof(IQueryable<>).MakeGenericType(element));
            }
            return base.VisitExtension(node);
        }
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.DeclaringType == typeof(LMSDbContext) && node.Member.Name == nameof(LMSDbContext.CurrentOrganizationId))
                return Expression.Constant(graph.Selected);
            if (node.Type.IsGenericType && node.Type.GetGenericTypeDefinition() == typeof(DbSet<>))
            {
                var element = node.Type.GenericTypeArguments[0];
                var query = typeof(TenantGraph).GetMethod(nameof(Visible))!.MakeGenericMethod(element).Invoke(graph, null);
                return Expression.Constant(query, typeof(IQueryable<>).MakeGenericType(element));
            }
            return base.VisitMember(node);
        }
    }
    public void Dispose() => db.Dispose();
}

public sealed class TenantFilterTests
{
    [Theory]
    [InlineData("null")][InlineData("foreign")][InlineData("missing-context")]
    public async Task Course_insert_rejects_missing_or_foreign_organization_before_any_SQL(string scenario)
    {
        using var db = TenantSqlTests.CreateContext(scenario == "missing-context" ? null : TenantSqlTests.Scope(Guid.NewGuid()));
        var course = new Course("Title", "CODE", "Description", Guid.NewGuid(), CourseLevel.Beginner);
        if (scenario == "foreign") course.AssignOrganization(Guid.NewGuid());
        db.Courses.Add(course);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
    }

    public static TheoryData<Type> Entities => new()
    {
        typeof(Course), typeof(CourseSection), typeof(Lesson), typeof(Quiz), typeof(QuizQuestion),
        typeof(QuizOption), typeof(CourseEnrollment), typeof(LessonProgress), typeof(QuizAttempt),
        typeof(QuizAttemptAnswer), typeof(QuizAttemptAnswerOption)
    };

    [Theory, MemberData(nameof(Entities))]
    public void Lists_and_direct_ID_queries_exclude_other_organization_and_legacy_rows(Type entity)
    {
        using var graph = new TenantGraph();
        var method = typeof(TenantGraph).GetMethod(nameof(TenantGraph.Visible))!.MakeGenericMethod(entity);
        object[] Read() => ((System.Collections.IEnumerable)method.Invoke(graph, null)!).Cast<object>().ToArray();
        var a = Assert.Single(Read());
        graph.Selected = graph.B;
        var b = Assert.Single(Read());
        Assert.NotEqual(entity.GetProperty("Id")!.GetValue(a), entity.GetProperty("Id")!.GetValue(b));
        Assert.DoesNotContain(a, Read());
        graph.Selected = Guid.Empty;
        Assert.Empty(Read());
    }

    [Fact]
    public void SuperAdmin_has_no_implicit_all_organization_query_bypass()
    {
        using var graph = new TenantGraph(platform: true);
        Assert.Equal(graph.First.Course.Id, Assert.Single(graph.Visible<Course>()).Id);
        graph.Selected = graph.B;
        Assert.Equal(graph.Second.Course.Id, Assert.Single(graph.Visible<Course>()).Id);
        graph.Selected = Guid.Empty;
        Assert.Empty(graph.Visible<Course>());
    }
}
