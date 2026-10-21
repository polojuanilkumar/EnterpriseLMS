using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Tests;

public sealed class OrganizationAdministrationTests
{
    [Fact] public void API_member_routes_require_scoped_Admin_and_mutations_are_rate_limited()
    {
        var type=typeof(LMS.Api.Controllers.OrganizationsController);
        foreach(var name in new[]{"Members","Member","AddMember","SetMember"})
        {
            var method=type.GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<LMS.Api.Middleware.OrganizationScopeAttribute>());
            Assert.Equal("Admin,SuperAdmin",method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
            if(name is "AddMember" or "SetMember")Assert.NotNull(method.GetCustomAttribute<EnableRateLimitingAttribute>());
        }
        foreach(var name in new[]{"PlatformContext","PlatformList","Create"})
        {
            var method=type.GetMethod(name)!;Assert.Null(method.GetCustomAttribute<LMS.Api.Middleware.OrganizationScopeAttribute>());
            Assert.Equal("SuperAdmin",method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        }
    }
    [Fact] public async Task API_confirmation_is_required_even_when_callers_bypass_MVC()
    {
        var scope=TenantSqlTests.Scope(Guid.NewGuid(),OrganizationRole.Admin);var repo=new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var controller=new LMS.Api.Controllers.OrganizationsController(new(repo.Object,scope));
        await Assert.ThrowsAsync<ArgumentException>(()=>controller.SetMember(scope.OrganizationId!.Value,Guid.NewGuid(),new(OrganizationRole.Admin),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>controller.AddMember(scope.OrganizationId!.Value,new("a@example.test",OrganizationRole.Admin),default));repo.VerifyNoOtherCalls();
    }
    [Fact] public async Task Member_detail_never_accepts_a_foreign_parent_or_a_different_user()
    {
        var scope=TenantSqlTests.Scope(Guid.NewGuid(),OrganizationRole.Admin);var repo=new Mock<IOrganizationRepository>();var user=Guid.NewGuid();var h=new OrganizationHandlers(repo.Object,scope);
        repo.Setup(x=>x.MemberAsync(scope.OrganizationId!.Value,user,default)).ReturnsAsync(new OrganizationMemberRow(Guid.NewGuid(),user,"Secret","Name","secret@example.test",OrganizationRole.Admin));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>h.MemberAsync(scope.OrganizationId!.Value,user));
        repo.Setup(x=>x.MemberAsync(scope.OrganizationId!.Value,user,default)).ReturnsAsync(new OrganizationMemberRow(scope.OrganizationId!.Value,Guid.NewGuid(),"Secret","Name","secret@example.test",OrganizationRole.Admin));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>h.MemberAsync(scope.OrganizationId!.Value,user));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.MemberAsync(Guid.NewGuid(),user));
    }
    [Fact] public async Task Malformed_initial_membership_is_rejected_before_any_transaction_or_connection()
    {
        using var db=TenantSqlTests.CreateContext();var repo=new OrganizationRepository(db,new());var org=new Organization("A","Name");
        await Assert.ThrowsAsync<ArgumentException>(()=>repo.AddAsync(org,new OrganizationMembership(Guid.NewGuid(),Guid.NewGuid(),OrganizationRole.Admin)));
        await Assert.ThrowsAsync<ArgumentException>(()=>repo.AddAsync(org,new OrganizationMembership(org.Id,Guid.NewGuid(),OrganizationRole.Student)));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact] public void Existing_unique_indexes_and_restrictive_membership_foreign_keys_are_retained()
    {
        using var db=TenantSqlTests.CreateContext();var org=db.Model.FindEntityType(typeof(Organization))!;var member=db.Model.FindEntityType(typeof(OrganizationMembership))!;
        Assert.Contains(org.GetIndexes(),x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"Code"}));
        Assert.Contains(member.GetIndexes(),x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"OrganizationId","UserId"}));
        Assert.All(member.GetForeignKeys(),x=>Assert.Equal(DeleteBehavior.Restrict,x.DeleteBehavior));Assert.False(db.Database.HasPendingModelChanges());
    }
    [Theory]
    [InlineData(OrganizationRole.Admin,OrganizationRole.Student,1,false,false,"last")]
    [InlineData(OrganizationRole.Admin,OrganizationRole.Instructor,0,false,false,"last")]
    [InlineData(OrganizationRole.Instructor,OrganizationRole.Admin,2,true,false,"Reassign")]
    [InlineData(OrganizationRole.Instructor,OrganizationRole.Student,2,true,false,"Reassign")]
    [InlineData(OrganizationRole.Student,OrganizationRole.Instructor,2,false,true,"Active")]
    [InlineData(OrganizationRole.Student,OrganizationRole.Admin,2,false,true,"Active")]
    public void Role_changes_reject_unresolved_prerequisites(OrganizationRole oldRole,OrganizationRole role,int admins,bool assigned,bool active,string reason)
        => Assert.Contains(reason,Assert.Throws<InvalidOperationException>(()=>MembershipRules.Check(oldRole,role,admins,assigned,active)).Message);

    [Theory]
    [InlineData(OrganizationRole.Admin,OrganizationRole.Student,2,true,true)]
    [InlineData(OrganizationRole.Instructor,OrganizationRole.Admin,1,false,true)]
    [InlineData(OrganizationRole.Student,OrganizationRole.Instructor,1,true,false)]
    [InlineData(OrganizationRole.Admin,OrganizationRole.Admin,1,false,false)]
    [InlineData(OrganizationRole.Instructor,OrganizationRole.Instructor,1,true,false)]
    [InlineData(OrganizationRole.Student,OrganizationRole.Student,1,false,true)]
    public void Resolved_and_unchanged_roles_are_permitted(OrganizationRole oldRole,OrganizationRole role,int admins,bool assigned,bool active)
        => MembershipRules.Check(oldRole,role,admins,assigned,active);

    [Fact]
    public void Exact_email_looks_up_at_most_two_IDs_and_never_prefix_matches()
    {
        var users=new[]{User("a@example.test"),User("ab@example.test"),User("a@example.test")}.AsQueryable();
        var normalized=MembershipRules.NormalizeEmail(" A@example.test ");
        Assert.Equal("A@EXAMPLE.TEST",normalized);
        var matches=OrganizationAdministrationQueries.ExactAccount(users,normalized).ToArray();
        Assert.Equal(2,matches.Length);Assert.DoesNotContain(users.Single(x=>x.Email=="ab@example.test").Id,matches);
        Assert.Empty(OrganizationAdministrationQueries.ExactAccount(users,"MISSING@EXAMPLE.TEST"));
    }
    [Theory][InlineData("")][InlineData("prefix")][InlineData("Name <a@example.test>")]
    public void Assignment_rejects_nonexact_email_input(string email)=>Assert.Throws<ArgumentException>(()=>MembershipRules.NormalizeEmail(email));

    [Fact]
    public void Members_filters_counts_and_pagination_use_only_selected_organization()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var first=User("a@example.test","Ada");var second=User("b@example.test","Bea");var other=User("secret@example.test","Ada");
        var memberships=new[]{new OrganizationMembership(a,first.Id,OrganizationRole.Admin),new(a,second.Id,OrganizationRole.Student),new(b,first.Id,OrganizationRole.Student),new(b,other.Id,OrganizationRole.Admin)}.AsQueryable();
        var users=new[]{first,second,other}.AsQueryable();
        var all=OrganizationAdministrationQueries.Members(memberships,users,a,new());Assert.Equal(2,all.Count());Assert.All(all,x=>Assert.Equal(a,x.OrganizationId));
        Assert.DoesNotContain(all,x=>x.Email==other.Email);
        var filtered=OrganizationAdministrationQueries.Members(memberships,users,a,new(Search:"Ada",Role:OrganizationRole.Admin));
        Assert.Equal(first.Id,Assert.Single(filtered).UserId);
        Assert.Equal(second.Id,all.OrderBy(x=>x.FirstName).Skip(1).Take(1).Single().UserId);
        Assert.Empty(OrganizationAdministrationQueries.Members(memberships,users,a,new(Search:"secret")));
    }

    [Fact]
    public void Safeguard_queries_ignore_other_org_work_and_completed_cancelled_history()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var user=Guid.NewGuid();
        var c1=Course(a,user);var c2=Course(b,user);
        var completed=new CourseEnrollment(c1.Id,user);completed.Complete();var cancelled=new CourseEnrollment(c1.Id,user);cancelled.Cancel();
        var foreign=new CourseEnrollment(c2.Id,user);var another=new CourseEnrollment(c1.Id,Guid.NewGuid());
        var courses=new[]{c1,c2}.AsQueryable();var enrollments=new[]{completed,cancelled,foreign,another}.AsQueryable();
        Assert.Equal(c1.Id,Assert.Single(OrganizationAdministrationQueries.AssignedCourses(courses,a,user)));
        Assert.Empty(OrganizationAdministrationQueries.ActiveEnrollments(enrollments,courses,a,user));
        var membership=new OrganizationMembership(a,user,OrganizationRole.Student);var other=new OrganizationMembership(b,user,OrganizationRole.Admin);
        MembershipRules.Check(membership.Role,OrganizationRole.Instructor,1,false,false);membership.ChangeRole(OrganizationRole.Instructor);
        Assert.Equal(OrganizationRole.Admin,other.Role);Assert.Equal(EnrollmentStatus.Completed,completed.Status);Assert.Equal(EnrollmentStatus.Cancelled,cancelled.Status);Assert.NotNull(completed.CompletedAt);
    }

    [Theory][InlineData(0,20)][InlineData(10001,20)][InlineData(1,0)][InlineData(1,51)]
    public void Pagination_is_bounded(int page,int size)=>Assert.Throws<ArgumentException>(()=>new OrganizationListFilter(page,size).Validate(true));
    [Fact] public void Invalid_search_and_roles_are_rejected()
    {Assert.Throws<ArgumentException>(()=>new OrganizationListFilter(Search:new string('x',101)).Validate(true));Assert.Throws<ArgumentException>(()=>new OrganizationListFilter(Role:(OrganizationRole)4).Validate(true));Assert.Throws<ArgumentException>(()=>new OrganizationListFilter(Role:OrganizationRole.Admin).Validate(false));}

    [Theory][InlineData(OrganizationRole.Student)][InlineData(OrganizationRole.Instructor)]
    public async Task Stored_nonadmin_and_foreign_org_cannot_assign_email_or_list(OrganizationRole role)
    {
        var repo=new Mock<IOrganizationRepository>(MockBehavior.Strict);var scope=TenantSqlTests.Scope(Guid.NewGuid(),role);var h=new OrganizationHandlers(repo.Object,scope);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.AddMemberByEmailAsync(scope.OrganizationId!.Value,"a@example.test",OrganizationRole.Admin,true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.MembersPageAsync(Guid.NewGuid(),new()));repo.VerifyNoOtherCalls();
    }
    [Fact] public async Task Email_assignment_requires_confirmation_and_selected_platform_target()
    {
        var repo=new Mock<IOrganizationRepository>(MockBehavior.Strict);var scope=TenantSqlTests.Scope(Guid.NewGuid(),OrganizationRole.Admin,true);var h=new OrganizationHandlers(repo.Object,scope);
        await Assert.ThrowsAsync<ArgumentException>(()=>h.AddMemberByEmailAsync(scope.OrganizationId!.Value,"a@example.test",OrganizationRole.Student,false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.AddMemberByEmailAsync(Guid.NewGuid(),"a@example.test",OrganizationRole.Student,true));
        repo.Setup(x=>x.AddMemberByEmailAsync(scope.OrganizationId!.Value,"A@EXAMPLE.TEST",OrganizationRole.Student,default)).Returns(Task.CompletedTask);
        await h.AddMemberByEmailAsync(scope.OrganizationId!.Value," a@example.test ",OrganizationRole.Student,true);
        repo.VerifyAll();
    }
    [Fact] public async Task Organization_creation_by_email_has_one_atomic_repository_write_and_generic_missing_response()
    {
        var repo=new Mock<IOrganizationRepository>();var actor=Guid.NewGuid();var admin=Guid.NewGuid();OrganizationMembership? saved=null;
        repo.Setup(x=>x.IsSuperAdminAsync(actor,default)).ReturnsAsync(true);repo.Setup(x=>x.FindAssignableByEmailAsync("ADMIN@EXAMPLE.TEST",default)).ReturnsAsync(admin);repo.Setup(x=>x.UserExistsAsync(admin,default)).ReturnsAsync(true);
        repo.Setup(x=>x.AddAsync(It.IsAny<Organization>(),It.IsAny<OrganizationMembership>(),default)).Callback((Organization _,OrganizationMembership m,CancellationToken _)=>saved=m).Returns(Task.CompletedTask);
        var h=new OrganizationHandlers(repo.Object,new());var result=await h.CreateByEmailAsync(actor,"code","Name","admin@example.test",true);
        Assert.Equal(result.Id,saved!.OrganizationId);Assert.Equal(admin,saved.UserId);Assert.Equal(OrganizationRole.Admin,saved.Role);Assert.NotEqual(actor,saved.UserId);
        repo.Verify(x=>x.AddAsync(It.IsAny<Organization>(),It.IsAny<OrganizationMembership>(),default),Times.Once);
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>h.CreateByEmailAsync(actor,"code2","Name","missing@example.test",true));Assert.Equal(MembershipRules.Unavailable,ex.Message);
    }
    [Fact] public async Task Account_disappearing_after_email_resolution_returns_the_same_generic_conflict()
    {
        var repo=new Mock<IOrganizationRepository>();var actor=Guid.NewGuid();var user=Guid.NewGuid();
        repo.Setup(x=>x.IsSuperAdminAsync(actor,default)).ReturnsAsync(true);
        repo.Setup(x=>x.FindAssignableByEmailAsync("EXISTING@EXAMPLE.TEST",default)).ReturnsAsync(user);
        var h=new OrganizationHandlers(repo.Object,new());var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>h.CreateByEmailAsync(actor,"CODE","Name","existing@example.test",true));
        Assert.Equal(MembershipRules.Unavailable,error.Message);repo.Verify(x=>x.AddAsync(It.IsAny<Organization>(),It.IsAny<OrganizationMembership>(),default),Times.Never);
    }
    [Fact] public async Task Platform_management_rechecks_stored_privileges_without_selected_org()
    {
        var repo=new Mock<IOrganizationRepository>();var actor=Guid.NewGuid();var h=new OrganizationHandlers(repo.Object,new());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.PlatformPageAsync(actor,new()));
        repo.Setup(x=>x.IsSuperAdminAsync(actor,default)).ReturnsAsync(true);repo.Setup(x=>x.PlatformPageAsync(It.IsAny<OrganizationListFilter>(),default)).ReturnsAsync(new OrganizationListPage<PlatformOrganizationRow>(null,1,20,0,[]));
        Assert.Empty((await h.PlatformPageAsync(actor,new())).Items);
        repo.Setup(x=>x.IsSuperAdminAsync(actor,default)).ReturnsAsync(false);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>h.CreateByEmailAsync(actor,"C","N","a@example.test",true));
    }
    [Theory][InlineData(2601,"'IX_Organizations_Code'",true)][InlineData(2627,"'IX_OrganizationMemberships_OrganizationId_UserId'",true)][InlineData(2601,"'OtherIndex'",false)][InlineData(1205,"'IX_Organizations_Code'",false)][InlineData(2627,"IX_Organizations_Code_extra",false)]
    public void Only_named_organization_uniqueness_errors_are_expected(int number,string message,bool expected)
        =>Assert.Equal(expected,OrganizationRepository.IsExpectedUnique(number,message));

    [Theory][InlineData("members")][InlineData("platform")][InlineData("email")][InlineData("admins")][InlineData("courses")][InlineData("enrollments")][InlineData("lock")]
    public void Production_queries_translate_without_connections_and_select_no_secrets(string kind)
    {
        var org=Guid.NewGuid();var scope=TenantSqlTests.Scope(org,OrganizationRole.Admin);using var db=TenantSqlTests.CreateContext(scope);
        string sql=kind switch {
            "members"=>OrganizationAdministrationQueries.Members(db.OrganizationMemberships.AsNoTracking(),db.Users.AsNoTracking(),org,new(2,10,"needle",OrganizationRole.Student)).OrderBy(x=>x.UserId).Skip(10).Take(10).ToQueryString(),
            "platform"=>OrganizationAdministrationQueries.Organizations(db.Organizations.AsNoTracking(),new(2,10,"needle")).OrderBy(x=>x.Code).Skip(10).Take(10).ToQueryString(),
            "email"=>OrganizationAdministrationQueries.ExactAccount(db.Users.AsNoTracking(),"EXACT@EXAMPLE.TEST").ToQueryString(),
            "admins"=>OrganizationAdministrationQueries.Admins(db.OrganizationMemberships,org).ToQueryString(),
            "courses"=>OrganizationAdministrationQueries.AssignedCourses(db.Courses.IgnoreQueryFilters(),org,scope.UserId).ToQueryString(),
            "enrollments"=>OrganizationAdministrationQueries.ActiveEnrollments(db.CourseEnrollments.IgnoreQueryFilters(),db.Courses.IgnoreQueryFilters(),org,scope.UserId).ToQueryString(),
            _=>db.OrganizationWriteLock(org).ToQueryString()};
        Assert.DoesNotContain("PasswordHash",sql);Assert.DoesNotContain("SecurityStamp",sql);Assert.DoesNotContain("ConcurrencyStamp",sql);Assert.DoesNotContain("AssignmentSubmissions",sql);
        if(kind is "members" or "platform"){Assert.Contains("OFFSET",sql);Assert.Contains("FETCH NEXT",sql);}
        if(kind is "members" or "admins" or "courses" or "enrollments"){Assert.Contains(org.ToString(),sql);Assert.Contains("OrganizationId",sql);}
        if(kind=="email"){Assert.Contains("NormalizedEmail",sql);Assert.DoesNotContain("LIKE",sql);Assert.Contains("TOP",sql);}
        if(kind=="lock"){Assert.Contains("UPDLOCK,HOLDLOCK",sql);Assert.Contains(org.ToString(),sql);}
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private static ApplicationUser User(string email,string first="Name")=>new(){Id=Guid.NewGuid(),Email=email,NormalizedEmail=email.ToUpperInvariant(),FirstName=first,LastName="Last",PasswordHash="never expose",SecurityStamp="never expose"};
    private static Course Course(Guid org,Guid user){var c=new Course("Title","CODE","Desc",user,CourseLevel.Beginner);c.AssignOrganization(org);return c;}
}
