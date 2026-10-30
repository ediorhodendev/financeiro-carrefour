using Financial.Api.Messaging;
using Financial.Api.Development;
using Financial.Api.Endpoints;
using Financial.Api.Errors;
using Financial.Api.OpenApi;
using Financial.Application.Movements;
using Financial.Application.Provisioning;
using Financial.Application.Queries;
using Financial.Domain.Accounts;
using Financial.Domain.Customers;
using Financial.Domain.Movements;
using Financial.Infrastructure.Movements;
using Financial.Infrastructure.Persistence;
using Financial.Infrastructure.Provisioning;
using Financial.Infrastructure.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var demo = DemoAccess.Create(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddDbContextFactory<FinancialDbContext>(options => options.UseNpgsql(
    DatabaseConnection(builder.Configuration),
    postgres => postgres.CommandTimeout(15)));
builder.Services.AddScoped<IAccountTransactionFactory, PostgresAccountTransactionFactory>();
builder.Services.AddScoped<RegisterMovementHandler>();
builder.Services.AddScoped<ReverseMovementHandler>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<IAccountQueryStore, PostgresAccountQueryStore>();
builder.Services.AddScoped<AccountQueries>();
builder.Services.AddScoped<IProvisioningStore, PostgresProvisioningStore>();
builder.Services.AddScoped<ProvisioningHandler>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    if (demo is not null) { demo.Configure(options); return; }
    options.TokenValidationParameters.RoleClaimType = "role";
    var authority = builder.Configuration["Jwt:Authority"];
    if (!string.IsNullOrWhiteSpace(authority))
    { options.Authority = authority; options.Audience = builder.Configuration["Jwt:Audience"]; }
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Financeiro Carrefour API", Version = "v1", Description = "Credits, debits and historical balances. Use a local development JWT via Authorize. All amounts are BRL. Movement type codes: 1 = Credit, 2 = Debit." });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Paste only the JWT token (without the Bearer prefix)." });
    options.OperationFilter<FinancialOperationFilter>();
    options.SchemaFilter<FinancialSchemaFilter>();
    options.SupportNonNullableReferenceTypes();
});
builder.Services.AddSingleton<Financial.Infrastructure.Messaging.ReceiptStore>();
if (builder.Configuration.GetValue<bool>("Messaging:Enabled"))
    builder.Services.AddHostedService<MovementMessagingWorker>();
var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FinancialDbContext>().Database.MigrateAsync();
    return;
}
if (args.Contains("--seed-demo", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo seed is only available in Development.");
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<FinancialDbContext>();
    var customer = await db.Customers.SingleOrDefaultAsync(c => c.SubjectId == "demo-operator");
    if (customer is null)
    {
        customer = new Customer("Demo Customer", "demo-operator", DateTimeOffset.UtcNow);
        db.Customers.Add(customer);
        db.Accounts.Add(new Account(customer.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }
    var account = await db.Accounts.SingleOrDefaultAsync(a => a.CustomerId == customer.Id);
    if (account is null)
    {
        account = new Account(customer.Id, DateTimeOffset.UtcNow);
        db.Accounts.Add(account); await db.SaveChangesAsync();
    }
    Console.WriteLine($"DEMO_ACCOUNT_ID={account.Id}");
    return;
}
app.Use(async (context, next) =>
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    try { await next(context); }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {StatusCode} in {ElapsedMs}ms TraceId={TraceId}",
            context.Request.Method, context.Response.StatusCode, timer.Elapsed.TotalMilliseconds, context.TraceIdentifier);
    }
});
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).WithTags("Health");
app.MapGet("/health/ready", async (FinancialDbContext db, CancellationToken ct) =>
{
    // Readiness checks schema availability too, without returning financial data.
    await db.Accounts.Select(a => a.Id).Take(1).ToArrayAsync(ct);
    return Results.Ok(new { status = "ready" });
}).WithTags("Health");
app.MapPost("/api/accounts/{accountId:guid}/movements", async (
    Guid accountId, MovementRequest request, HttpContext context, RegisterMovementHandler handler, CancellationToken ct) =>
{
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
    if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
        return Results.Problem(statusCode: 400, title: "Provide one Idempotency-Key header.");
    var result = await handler.ExecuteAsync(new RegisterMovementCommand(accountId, request.Type, request.Amount, keys[0] ?? "", subject), ct);
    return Results.Json(result, statusCode: result.Replayed ? 200 : 201);
}).RequireAuthorization(policy => policy.RequireRole("operator")).WithTags("Movements")
    .WithSummary("Register a credit or debit atomically; retries reuse Idempotency-Key.")
    .Produces<MovementResult>(201).Produces<MovementResult>(200).ProducesProblem(400)
    .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
if (demo is not null) demo.MapEndpoints(app);
app.MapGet("/api/accounts/{accountId:guid}/receipts", async (Guid accountId, HttpContext context,
    FinancialDbContext db, AccountQueries queries, CancellationToken ct) =>
{
    var subject = context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
    await queries.ExecuteAsync(accountId, subject, null, ct);
    var items = await db.ProcessingReceipts.AsNoTracking().Where(r => r.AccountId == accountId)
        .OrderByDescending(r => r.ProcessedAt).Take(100).ToListAsync(ct);
    return Results.Ok(items);
}).RequireAuthorization(policy => policy.RequireRole("operator")).WithTags("Messaging")
    .WithSummary("Latest 100 idempotent consumer receipts; eventually consistent.");
app.MapReversalEndpoints();
app.MapAccountReadEndpoints();
app.MapProvisioningEndpoints();
app.Run();
static string DatabaseConnection(IConfiguration configuration)
{
    var configured = configuration.GetConnectionString("Financial");
    if (!string.IsNullOrWhiteSpace(configured)) return configured;
    return new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = configuration["Database:Host"] ?? "postgres",
        Database = configuration["Database:Name"] ?? "financial",
        Username = configuration["Database:User"] ?? "financial_local",
        Password = configuration["Database:Password"] ?? throw new InvalidOperationException("Set ConnectionStrings__Financial or Database__Password.")
    }.ConnectionString;
}
public sealed record MovementRequest(MovementType Type, decimal Amount);
public partial class Program { }
