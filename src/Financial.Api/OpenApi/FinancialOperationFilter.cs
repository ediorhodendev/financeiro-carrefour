using Microsoft.AspNetCore.Authorization;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Financial.Api.OpenApi;
public sealed class FinancialOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }];
            (operation.Responses ??= new OpenApiResponses()).TryAdd("401", new OpenApiResponse { Description = "Missing or invalid token." });
            (operation.Responses ??= new OpenApiResponses()).TryAdd("403", new OpenApiResponse { Description = "Required role is absent." });
        }
        var path = context.ApiDescription.RelativePath ?? "";
        operation.Parameters ??= [];
        if ((path.EndsWith("/movements", StringComparison.Ordinal) || path.EndsWith("/reversal", StringComparison.Ordinal)) && context.ApiDescription.HttpMethod == "POST")
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                Description = "Reuse this key with the same payload after retries. New operation: new key. Replay 200; original 201; conflicting payload 409.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 100 }, Example = JsonValue.Create("credit-demo-001")
            });
        if (path.EndsWith("/balance", StringComparison.Ordinal))
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "at", In = ParameterLocation.Query, Required = false,
                Description = "Inclusive historical instant, ISO 8601 with Z or offset. Omit for current balance. Future and pre-opening instants are invalid.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
            });
        if (path.EndsWith("/movements", StringComparison.Ordinal) && context.ApiDescription.HttpMethod == "GET")
        {
            operation.Parameters.Add(new OpenApiParameter { Name = "limit", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "100", Default = JsonValue.Create(50) } });
            operation.Parameters.Add(new OpenApiParameter { Name = "cursor", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 1024 }, Description = "Use nextCursor from the previous page; live keyset pagination." });
        }
    }
}
