using System.Security.Claims;
using System.Text.Json;
using LMS.Api.Middleware;
using LMS.Application.Features.Organizations;
using LMS.Application.Interfaces.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class OrganizationSelectionTests
{
    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Organization A { get; } = new("A", "Alpha");
        public Organization B { get; } = new("B", "Beta");
        public Mock<IOrganizationRepository> Repository { get; } = new(MockBehavior.Strict);
        public Fixture()
        {
            Repository.Setup(x => x.GetAsync(A.Id, It.IsAny<CancellationToken>())).ReturnsAsync(A);
            Repository.Setup(x => x.GetAsync(B.Id, It.IsAny<CancellationToken>())).ReturnsAsync(B);
            Repository.Setup(x => x.GetMembershipAsync(A.Id, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrganizationMembership(A.Id, UserId, OrganizationRole.Admin));
            Repository.Setup(x => x.GetMembershipAsync(B.Id, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrganizationMembership(B.Id, UserId, OrganizationRole.Student));
            Repository.Setup(x => x.IsSuperAdminAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        }
        public DefaultHttpContext Request(Guid? organization = null, bool authenticated = true, bool scoped = true)
        {
            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Role, "Instructor"),
                new Claim(ClaimTypes.Role, "SuperAdmin"), new Claim(ClaimTypes.Email, "synthetic@example.test")
            }, authenticated ? "Test" : null));
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                scoped ? new EndpointMetadataCollection(new OrganizationScopeAttribute()) : new EndpointMetadataCollection(), "test"));
            if (organization.HasValue) http.Request.Headers["X-Organization-Id"] = organization.ToString();
            return http;
        }
        public async Task<(OrganizationContext Context, bool Called)> Run(DefaultHttpContext http)
        {
            var context = new OrganizationContext();
            var handlers = new OrganizationHandlers(Repository.Object, context);
            var called = false;
            var middleware = new OrganizationMiddleware(_ => { called = true; return Task.CompletedTask; });
            var errors = new GlobalExceptionMiddleware(c => middleware.InvokeAsync(c, handlers, context, Repository.Object),
                NullLogger<GlobalExceptionMiddleware>.Instance, Options.Create(new JsonOptions()));
            await errors.InvokeAsync(http);
            return (context, called);
        }
    }

    [Fact]
    public async Task Stored_roles_change_with_selected_organization_and_replace_global_claims()
    {
        var f = new Fixture();
        foreach (var (organization, role) in new[] { (f.A, OrganizationRole.Admin), (f.B, OrganizationRole.Student) })
        {
            var http = f.Request(organization.Id);
            var (context, called) = await f.Run(http);
            Assert.True(called);
            Assert.Equal(organization.Id, context.OrganizationId);
            Assert.Equal(role, context.Role);
            Assert.Equal(f.UserId, context.UserId);
            Assert.Equal(new[] { role.ToString() }, http.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray());
            Assert.Equal("synthetic@example.test", http.User.FindFirstValue(ClaimTypes.Email));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_or_invalid_header_is_400_before_repository_access(string? header)
    {
        var f = new Fixture(); var http = f.Request();
        if (header is not null) http.Request.Headers["X-Organization-Id"] = header;
        var (context, called) = await f.Run(http);
        Assert.False(called); Assert.Null(context.OrganizationId);
        Assert.Equal(400, http.Response.StatusCode);
        f.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Multiple_selection_headers_are_rejected()
    {
        var f = new Fixture(); var http = f.Request();
        http.Request.Headers["X-Organization-Id"] = new StringValues(new[] { f.A.Id.ToString(), f.B.Id.ToString() });
        Assert.False((await f.Run(http)).Called);
        Assert.Equal(400, http.Response.StatusCode);
        f.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Global_admin_and_superadmin_claims_do_not_replace_missing_membership()
    {
        var f = new Fixture(); var http = f.Request(f.B.Id);
        f.Repository.Setup(x => x.GetMembershipAsync(f.B.Id, f.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationMembership?)null);
        var (context, called) = await f.Run(http);
        Assert.False(called); Assert.Null(context.OrganizationId);
        Assert.Equal(403, http.Response.StatusCode);
        using var body = JsonDocument.Parse(((MemoryStream)http.Response.Body).ToArray());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Organization access denied.", body.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_inactive_organization_is_denied_even_for_platform_admin(bool inactive)
    {
        var f = new Fixture();
        f.Repository.Setup(x => x.IsSuperAdminAsync(f.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        if (inactive)
            typeof(Organization).GetProperty(nameof(Organization.IsActive))!.SetValue(f.B, false); // Stored inactive row fixture.
        else f.Repository.Setup(x => x.GetAsync(f.B.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);
        var http = f.Request(f.B.Id);
        Assert.False((await f.Run(http)).Called);
        Assert.Equal(403, http.Response.StatusCode);
        f.Repository.Verify(x => x.GetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stored_platform_admin_can_select_target_without_becoming_a_student()
    {
        var f = new Fixture(); var http = f.Request(f.B.Id);
        f.Repository.Setup(x => x.IsSuperAdminAsync(f.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        f.Repository.Setup(x => x.GetMembershipAsync(f.B.Id, f.UserId, It.IsAny<CancellationToken>())).ReturnsAsync((OrganizationMembership?)null);
        var (context, called) = await f.Run(http);
        Assert.True(called); Assert.True(context.IsSuperAdmin); Assert.Null(context.Role);
        Assert.Equal(f.B.Id, context.OrganizationId);
        Assert.True(http.User.IsInRole("SuperAdmin"));
        Assert.False(http.User.IsInRole("Student"));
        Assert.False(http.User.IsInRole("Admin"));
    }

    [Fact]
    public async Task Platform_admin_must_still_select_an_organization_for_scoped_requests()
    {
        var f = new Fixture();
        f.Repository.Setup(x => x.IsSuperAdminAsync(f.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var http = f.Request();
        Assert.False((await f.Run(http)).Called);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_scoped_request_is_401_without_database_work()
    {
        var f = new Fixture(); var http = f.Request(f.A.Id, authenticated: false);
        Assert.False((await f.Run(http)).Called);
        Assert.Equal(401, http.Response.StatusCode);
        f.Repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_authenticated_user_is_401(string userId)
    {
        var f = new Fixture(); var http = f.Request(f.A.Id);
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"));
        Assert.False((await f.Run(http)).Called);
        Assert.Equal(401, http.Response.StatusCode);
        f.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Selection_is_immutable_inside_one_request()
    {
        var f = new Fixture(); var context = new OrganizationContext();
        var handlers = new OrganizationHandlers(f.Repository.Object, context);
        await handlers.SelectAsync(f.A.Id, f.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handlers.SelectAsync(f.B.Id, f.UserId));
        Assert.Equal(f.A.Id, context.OrganizationId);
        Assert.Equal(OrganizationRole.Admin, context.Role);
    }

    [Fact]
    public async Task Non_scoped_request_does_not_gain_organization_privileges_from_global_claims()
    {
        var f = new Fixture(); var http = f.Request(scoped: false);
        var (context, called) = await f.Run(http);
        Assert.True(called); Assert.Null(context.OrganizationId);
        Assert.Empty(http.User.FindAll(ClaimTypes.Role));
        f.Repository.Verify(x => x.GetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
