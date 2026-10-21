using System.Reflection;
using System.Security.Claims;
using LMS.Api.Controllers;
using LMS.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LMS.Tests;

public sealed class TenantEndpointTests
{
    public static IEnumerable<object[]> Routes()
    {
        foreach (var type in new[] { typeof(CoursesController), typeof(CourseSectionsController), typeof(LessonsController),
            typeof(QuizQuestionsController), typeof(QuizAttemptsController), typeof(EnrollmentsController) })
            foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
                yield return new object[] { type, method.Name };
    }

    [Theory, MemberData(nameof(Routes))]
    public async Task Every_learning_route_requires_verified_scope_and_authentication(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        var attributes = controller.GetCustomAttributes().Concat(method.GetCustomAttributes()).ToList();
        Assert.Contains(attributes, x => x is OrganizationScopeAttribute);
        Assert.DoesNotContain(attributes, x => x is AllowAnonymousAttribute);
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        using (services)
        {
            var policy = await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), attributes.OfType<IAuthorizeData>());
            Assert.NotNull(policy);
            var authorization = services.GetRequiredService<IAuthorizationService>();
            Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy!)).Succeeded);
            // These roles are the output of OrganizationMiddleware, not incoming JWT roles.
            foreach (var role in new[] { "Student", "Instructor", "Admin", "SuperAdmin" })
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "VerifiedOrganization"));
                var expected = attributes.OfType<AuthorizeAttribute>().All(a => string.IsNullOrEmpty(a.Roles) || a.Roles.Split(',').Contains(role));
                Assert.Equal(expected, (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded);
            }
        }
    }
}
