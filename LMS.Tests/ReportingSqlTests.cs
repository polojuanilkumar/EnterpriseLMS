using LMS.Application.Features.Reporting;
using LMS.Domain.Enums;
using LMS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace LMS.Tests;
public sealed class ReportingSqlTests
{
    [Theory][InlineData("summary")][InlineData("learners")][InlineData("gradecourses")][InlineData("quizzes")][InlineData("assignments")]
    public void Production_queries_translate_with_tenant_filters_bounded_paging_and_minimal_fields(string kind)
    {
        var org=Guid.NewGuid();var role=kind is "summary" or "learners"?OrganizationRole.Instructor:OrganizationRole.Student;var scope=TenantSqlTests.Scope(org,role);using var db=TenantSqlTests.CreateContext(scope);var q=new ReportingQueries(ReportingSources.From(db),scope);var course=Guid.NewGuid();
        var sql=kind switch{
            "summary"=>q.Summary(new(2,10,"needle",true,1)).OrderBy(x=>x.Title).ThenBy(x=>x.CourseId).Skip(10).Take(10).ToQueryString(),
            "learners"=>q.Learners(course,new(2,10,"name",null,2)).OrderBy(x=>x.UserId).Skip(10).Take(10).ToQueryString(),
            "gradecourses"=>q.GradeCourses(new(2,10,null,null,2)).OrderBy(x=>x.CourseId).Skip(10).Take(10).ToQueryString(),
            "quizzes"=>q.Quizzes(course,new(2,10,2),DateTime.UtcNow).OrderBy(x=>x.QuizId).ThenBy(x=>x.AttemptId).Skip(10).Take(10).ToQueryString(),
            _=>q.Assignments(course,new(2,10,2)).OrderBy(x=>x.AssignmentId).Skip(10).Take(10).ToQueryString()};
        Assert.Contains(org.ToString(),sql);Assert.Contains(scope.UserId.ToString(),sql);Assert.Contains("OFFSET",sql);Assert.Contains("FETCH NEXT",sql);Assert.DoesNotContain("PasswordHash",sql);Assert.DoesNotContain("SecurityStamp",sql);Assert.DoesNotContain("AttachmentId",sql);Assert.DoesNotContain("InstructionsSnapshot",sql);Assert.DoesNotContain("[Text]",sql);Assert.DoesNotContain("QuizOptions",sql);
        Assert.Empty(db.ChangeTracker.Entries());if(kind=="quizzes"){Assert.Contains("PassingPercentageSnapshot",sql);Assert.DoesNotContain("[PassingPercentage]",sql);}
    }
}
