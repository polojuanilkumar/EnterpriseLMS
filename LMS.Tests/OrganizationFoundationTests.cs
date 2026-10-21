using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class OrganizationFoundationTests
{
    [Fact]
    public void Organization_normalizes_code_and_keeps_a_separate_name()
    {
        var organization = new Organization("  alpha  ", "  Alpha College  ");
        Assert.Equal("ALPHA", organization.Code);
        Assert.Equal("Alpha College", organization.Name);
        Assert.NotEqual(Guid.Empty, organization.Id);
        Assert.True(organization.IsActive);
        Assert.Equal(Guid.Parse("6f085af1-c85b-47c1-bd1e-73435b3e0001"), Organization.LegacyId);
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("  ", "Name")]
    [InlineData("A", "")]
    [InlineData("A", "  ")]
    public void Empty_organization_fields_are_rejected(string code, string name)
        => Assert.Throws<ArgumentException>(() => new Organization(code, name));

    [Theory]
    [InlineData(51, 1)]
    [InlineData(1, 201)]
    public void Organization_lengths_match_schema_limits(int codeLength, int nameLength)
        => Assert.Throws<ArgumentException>(() => new Organization(new string('A', codeLength), new string('N', nameLength)));

    [Fact]
    public async Task Platform_admin_creates_organization_and_initial_admin_together()
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var actor = Guid.NewGuid(); var initialAdmin = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        var token = source.Token;
        repository.Setup(x => x.IsSuperAdminAsync(actor, token)).ReturnsAsync(true);
        repository.Setup(x => x.UserExistsAsync(initialAdmin, token)).ReturnsAsync(true);
        Organization? saved = null; OrganizationMembership? membership = null;
        repository.Setup(x => x.AddAsync(It.IsAny<Organization>(), It.IsAny<OrganizationMembership>(), token))
            .Callback((Organization o, OrganizationMembership m, CancellationToken _) => { saved = o; membership = m; })
            .Returns(Task.CompletedTask);
        var result = await new OrganizationHandlers(repository.Object, new()).CreateAsync(actor, "alpha", "Alpha", initialAdmin, token);
        Assert.Same(result, saved);
        Assert.NotNull(membership);
        Assert.Equal(result.Id, membership.OrganizationId);
        Assert.Equal(initialAdmin, membership.UserId);
        Assert.Equal(OrganizationRole.Admin, membership.Role);
        repository.Verify(x => x.AddAsync(result, membership, token), Times.Once);
    }

    [Fact]
    public async Task Organization_admin_cannot_create_platform_organizations()
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var actor = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(Guid.NewGuid(), actor, OrganizationRole.Admin, false);
        repository.Setup(x => x.IsSuperAdminAsync(actor, default)).ReturnsAsync(false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new OrganizationHandlers(repository.Object, context)
            .CreateAsync(actor, "B", "Other organization", Guid.NewGuid()));
        repository.Verify(x => x.AddAsync(It.IsAny<Organization>(), It.IsAny<OrganizationMembership>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_initial_admin_does_not_create_organization()
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var actor = Guid.NewGuid(); var user = Guid.NewGuid();
        repository.Setup(x => x.IsSuperAdminAsync(actor, default)).ReturnsAsync(true);
        repository.Setup(x => x.UserExistsAsync(user, default)).ReturnsAsync(false);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new OrganizationHandlers(repository.Object, new())
            .CreateAsync(actor, "A", "Alpha", user));
        repository.Verify(x => x.AddAsync(It.IsAny<Organization>(), It.IsAny<OrganizationMembership>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrganizationRole.Student)]
    [InlineData(OrganizationRole.Instructor)]
    [InlineData(OrganizationRole.Admin)]
    public async Task Organization_admin_can_assign_explicit_role_in_selected_organization(OrganizationRole role)
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var organization = Guid.NewGuid(); var user = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(organization, Guid.NewGuid(), OrganizationRole.Admin, false);
        repository.Setup(x => x.UserExistsAsync(user, default)).ReturnsAsync(true);
        repository.Setup(x => x.SetMembershipAsync(organization, user, role, default)).Returns(Task.CompletedTask);
        await new OrganizationHandlers(repository.Object, context).SetMemberAsync(organization, user, role);
        repository.Verify(x => x.SetMembershipAsync(organization, user, role, default), Times.Once);
    }

    [Theory]
    [InlineData(OrganizationRole.Student, false)]
    [InlineData(OrganizationRole.Instructor, false)]
    [InlineData(OrganizationRole.Admin, true)]
    public async Task Nonadmin_or_wrong_organization_cannot_list_or_assign_members(OrganizationRole role, bool wrongOrganization)
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var organization = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(organization, Guid.NewGuid(), role, false);
        var target = wrongOrganization ? Guid.NewGuid() : organization;
        var handlers = new OrganizationHandlers(repository.Object, context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handlers.SetMemberAsync(target, Guid.NewGuid(), OrganizationRole.Student));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handlers.MembersPageAsync(target,new()));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Platform_admin_still_needs_matching_selected_organization_for_membership_writes()
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var organization = Guid.NewGuid(); var user = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(organization, Guid.NewGuid(), null, true);
        var handlers = new OrganizationHandlers(repository.Object, context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handlers.SetMemberAsync(Guid.NewGuid(), user, OrganizationRole.Admin));
        repository.Setup(x => x.UserExistsAsync(user, default)).ReturnsAsync(true);
        repository.Setup(x => x.SetMembershipAsync(organization, user, OrganizationRole.Admin, default)).Returns(Task.CompletedTask);
        await handlers.SetMemberAsync(organization, user, OrganizationRole.Admin);
        repository.Verify(x => x.SetMembershipAsync(organization, user, OrganizationRole.Admin, default), Times.Once);
    }

    [Fact]
    public async Task Admin_cannot_demote_self()
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var organization = Guid.NewGuid(); var user = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(organization, user, OrganizationRole.Admin, false);
        repository.Setup(x => x.UserExistsAsync(user, default)).ReturnsAsync(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationHandlers(repository.Object, context)
            .SetMemberAsync(organization, user, OrganizationRole.Student));
        repository.Verify(x => x.SetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(999)]
    public async Task Invalid_roles_cannot_reach_repository(int role)
    {
        var repository = new Mock<IOrganizationRepository>(MockBehavior.Strict);
        var organization = Guid.NewGuid();
        var context = new OrganizationContext();
        context.Initialize(organization, Guid.NewGuid(), OrganizationRole.Admin, false);
        await Assert.ThrowsAsync<ArgumentException>(() => new OrganizationHandlers(repository.Object, context)
            .SetMemberAsync(organization, Guid.NewGuid(), (OrganizationRole)role));
        Assert.Throws<ArgumentException>(() => new OrganizationMembership(organization, Guid.NewGuid(), (OrganizationRole)role));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public void Memberships_allow_different_roles_in_two_organizations_without_changing_identity()
    {
        var user = Guid.NewGuid();
        var a = new OrganizationMembership(Guid.NewGuid(), user, OrganizationRole.Admin);
        var b = new OrganizationMembership(Guid.NewGuid(), user, OrganizationRole.Student);
        b.ChangeRole(OrganizationRole.Instructor);
        Assert.Equal(OrganizationRole.Admin, a.Role);
        Assert.Equal(OrganizationRole.Instructor, b.Role);
        Assert.Equal(a.UserId, b.UserId);
        Assert.NotEqual(a.OrganizationId, b.OrganizationId);
    }

    [Fact]
    public void Course_ownership_is_distinct_from_instructor_and_cannot_be_transferred()
    {
        var instructor = Guid.NewGuid(); var organization = Guid.NewGuid();
        var course = new Course("Course", "CODE", "Description", instructor, CourseLevel.Beginner);
        Assert.Null(course.OrganizationId);
        course.AssignOrganization(organization);
        Assert.Throws<InvalidOperationException>(() => course.AssignOrganization(Guid.NewGuid()));
        Assert.Equal(organization, course.OrganizationId);
        Assert.Equal(instructor, course.InstructorId);
    }
}
