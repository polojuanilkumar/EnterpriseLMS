using LMS.Api.Middleware;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LMS.Api.Swagger;

public sealed class OrganizationHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Use the same endpoint metadata as OrganizationMiddleware, including
        // controller-level and action-level OrganizationScope attributes.
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<OrganizationScopeAttribute>().Any())
            return;

        operation.Parameters ??= [];
        // Header names are case-insensitive. Replace existing declarations so
        // repeated application still yields one required, correctly typed header.
        foreach (var parameter in operation.Parameters.Where(p => p.In == ParameterLocation.Header
            && string.Equals(p.Name, "X-Organization-Id", StringComparison.OrdinalIgnoreCase)).ToArray())
            operation.Parameters.Remove(parameter);

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Organization-Id",
            In = ParameterLocation.Header,
            Required = true,
            Description = "ID of the active organization to select for this request. Required for all scoped accounts, including SuperAdmin.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
        });
    }
}
