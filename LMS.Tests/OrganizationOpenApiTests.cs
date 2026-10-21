using System.Reflection;
using LMS.Api.Controllers;
using LMS.Api.Swagger;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace LMS.Tests;

public sealed class OrganizationOpenApiTests
{
    [Theory]
    [InlineData(typeof(CoursesController), nameof(CoursesController.GetCourses))]
    [InlineData(typeof(OrganizationsController), nameof(OrganizationsController.Members))]
    [InlineData(typeof(OrganizationsController), nameof(OrganizationsController.SetMember))]
    public void Scoped_actions_have_one_required_uuid_header(Type controller, string action)
    {
        var operation = new OpenApiOperation();
        new OrganizationHeaderOperationFilter().Apply(operation, Context(controller, action));
        AssertHeader(operation);
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.Login))]
    [InlineData(typeof(OrganizationsController), nameof(OrganizationsController.Mine))]
    [InlineData(typeof(OrganizationsController), nameof(OrganizationsController.Create))]
    public void Unscoped_actions_are_unchanged(Type controller, string action)
    {
        var query = new OpenApiParameter { Name = "search", In = ParameterLocation.Query };
        var operation = new OpenApiOperation { Parameters = [query] };
        new OrganizationHeaderOperationFilter().Apply(operation, Context(controller, action));
        Assert.Same(query, Assert.Single(operation.Parameters));
    }

    [Fact]
    public void Repeated_application_deduplicates_headers_case_insensitively_and_preserves_other_parameters()
    {
        var query = new OpenApiParameter { Name = "X-Organization-Id", In = ParameterLocation.Query };
        var unrelated = new OpenApiParameter { Name = "X-Correlation-Id", In = ParameterLocation.Header };
        var operation = new OpenApiOperation
        {
            Parameters = [query, unrelated,
                new OpenApiParameter { Name = "x-organization-id", In = ParameterLocation.Header, Required = false },
                new OpenApiParameter { Name = "X-Organization-Id", In = ParameterLocation.Header }]
        };
        var context = Context(typeof(CoursesController), nameof(CoursesController.GetCourses));
        var filter = new OrganizationHeaderOperationFilter();
        filter.Apply(operation, context);
        filter.Apply(operation, context);
        AssertHeader(operation);
        Assert.Equal(3, operation.Parameters.Count);
        Assert.Contains(query, operation.Parameters);
        Assert.Contains(unrelated, operation.Parameters);
    }

    private static void AssertHeader(OpenApiOperation operation)
    {
        var header = Assert.Single(operation.Parameters!, p => p.In == ParameterLocation.Header
            && string.Equals(p.Name, "X-Organization-Id", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("X-Organization-Id", header.Name);
        Assert.True(header.Required);
        Assert.Equal(JsonSchemaType.String, header.Schema!.Type);
        Assert.Equal("uuid", header.Schema.Format);
        Assert.Null(header.Schema.Default);
    }

    private static OperationFilterContext Context(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        var description = new ApiDescription
        {
            ActionDescriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetTypeInfo(),
                MethodInfo = method,
                EndpointMetadata = controller.GetCustomAttributes().Concat(method.GetCustomAttributes()).Cast<object>().ToList()
            }
        };
        return new OperationFilterContext(description, null!, new SchemaRepository(), new OpenApiDocument(), method);
    }
}
