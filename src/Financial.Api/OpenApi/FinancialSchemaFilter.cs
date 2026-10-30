using Financial.Api.Endpoints;
using Financial.Domain.Accounts;
using Financial.Domain.Movements;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace Financial.Api.OpenApi;

public sealed class FinancialSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema input, SchemaFilterContext context)
    {
        if (input is not OpenApiSchema schema) return;
        ApplySchema(schema, context);
    }
    private static void ApplySchema(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(MovementType))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Format = "int32";
            schema.Description = "Operation code: 1 = Credit (adds funds), 2 = Debit (removes funds). Zero and other values are invalid.";
            schema.Enum = [JsonValue.Create(1), JsonValue.Create(2)];
        }
        if (context.Type == typeof(MovementRequest))
        {
            (schema.Required ??= new HashSet<string>()).Add("type");
            (schema.Required ??= new HashSet<string>()).Add("amount");
            if (schema.Properties is not null && schema.Properties.TryGetValue("amount", out var value) && value is OpenApiSchema amount)
            {
                amount.Minimum = "0.01";
                amount.Maximum = Account.MaximumBalance.ToString(System.Globalization.CultureInfo.InvariantCulture);
                amount.MultipleOf = 0.01m;
            }
            schema.Example = new JsonObject { ["type"] = JsonValue.Create(1), ["amount"] = JsonValue.Create(100) };
        }
        if (context.Type == typeof(ReversalRequest))
        {
            (schema.Required ??= new HashSet<string>()).Add("reason");
            if (schema.Properties is not null && schema.Properties.TryGetValue("reason", out var value) && value is OpenApiSchema reason)
            { reason.MinLength = 1; reason.MaxLength = 500; }
            schema.Example = new JsonObject { ["reason"] = JsonValue.Create("Movement registered incorrectly") };
        }
        if (context.Type == typeof(CreateCustomerRequest))
        {
            (schema.Required ??= new HashSet<string>()).Add("name");
            (schema.Required ??= new HashSet<string>()).Add("subjectId");
            schema.Example = new JsonObject { ["name"] = JsonValue.Create("Demo Customer"), ["subjectId"] = JsonValue.Create("demo-customer-01") };
        }
    }
}
