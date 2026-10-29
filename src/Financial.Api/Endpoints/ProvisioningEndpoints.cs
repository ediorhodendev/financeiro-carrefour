using Financial.Application.Provisioning;
namespace Financial.Api.Endpoints;
public sealed record CreateCustomerRequest(string Name, string SubjectId);
public static class ProvisioningEndpoints
{
    public static void MapProvisioningEndpoints(this WebApplication app)
    {
        app.MapPost("/api/customers", async (CreateCustomerRequest request, ProvisioningHandler handler, CancellationToken ct) =>
        {
            var customer = await handler.CreateCustomerAsync(request.Name, request.SubjectId, ct);
            return Results.Created($"/api/customers/{customer.Id}", customer);
        }).RequireAuthorization(p => p.RequireRole("admin")).WithTags("Customers")
            .WithSummary("Register a customer and bind an identity (administrator).")
            .Produces<CustomerResult>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(503);
        app.MapGet("/api/customers/{customerId:guid}", async (Guid customerId, IProvisioningStore store, CancellationToken ct) =>
        {
            var customer = await store.GetCustomerAsync(customerId, ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        }).RequireAuthorization(p => p.RequireRole("admin")).WithTags("Customers").Produces<CustomerResult>().Produces(404);
        app.MapPost("/api/customers/{customerId:guid}/accounts", async (Guid customerId, ProvisioningHandler handler, CancellationToken ct) =>
        {
            var account = await handler.OpenAccountAsync(customerId, ct);
            return Results.Created($"/api/accounts/{account.Id}", account);
        }).RequireAuthorization(p => p.RequireRole("admin")).WithTags("Accounts")
            .WithSummary("Open the customer's single BRL account with zero balance (administrator).")
            .Produces<AccountResult>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapGet("/api/accounts/{accountId:guid}", async (Guid accountId, HttpContext context, IProvisioningStore store, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            var account = await store.GetAccountAsync(accountId, subject, context.User.IsInRole("admin"), ct);
            return account is null ? Results.NotFound() : Results.Ok(account);
        }).RequireAuthorization().WithTags("Accounts").Produces<AccountResult>().Produces(404);
    }
}
