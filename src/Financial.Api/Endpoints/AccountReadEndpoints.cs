using System.Globalization;
using System.Text.RegularExpressions;
using Financial.Application.Movements;
using Financial.Application.Queries;
using Npgsql;
namespace Financial.Api.Endpoints;

public static partial class AccountReadEndpoints
{
    public static void MapAccountReadEndpoints(this WebApplication app)
    {
        app.MapGet("/api/accounts/{accountId:guid}/balance", async (
            Guid accountId, HttpContext context, AccountQueries queries, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            return await RespondAsync(async () =>
            {
                DateTimeOffset? at = null;
                if (context.Request.Query.TryGetValue("at", out var values))
                {
                    if (values.Count != 1 || !TryParseInstant(values[0], out var instant)) throw new ArgumentException("Use an ISO 8601 instant with Z or an explicit offset.");
                    at = instant;
                }
                return Results.Ok(await queries.ExecuteAsync(accountId, subject, at, ct));
            });
        }).RequireAuthorization().WithTags("Accounts").Produces<Financial.Application.Balances.BalanceResult>();
        app.MapGet("/api/accounts/{accountId:guid}/movements", async (
            Guid accountId, HttpContext context, AccountQueries queries, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            return await RespondAsync(async () =>
            {
                var limit = 50;
                if (context.Request.Query.TryGetValue("limit", out var limits) &&
                    (limits.Count != 1 || !int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)))
                    throw new ArgumentException("Invalid limit.");
                string? cursor = null;
                if (context.Request.Query.TryGetValue("cursor", out var cursors))
                {
                    if (cursors.Count != 1) throw new ArgumentException("Invalid cursor.");
                    cursor = cursors[0];
                }
                return Results.Ok(await queries.ListAsync(accountId, subject, limit, cursor, ct));
            });
        }).RequireAuthorization().WithTags("Movements").Produces<MovementPage>();
    }
    public static bool TryParseInstant(string? text, out DateTimeOffset instant)
    {
        instant = default;
        return text is not null && text.Length <= 40 && IsoInstant().IsMatch(text) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoInstant();
    private static async Task<IResult> RespondAsync(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: "Invalid query.", detail: ex.Message); }
        catch (AccountNotFoundException) { return Results.Problem(statusCode: 404, title: "Account not found."); }
        catch (InvalidOperationException ex) when (ex.InnerException is NpgsqlException)
        { return Results.Problem(statusCode: 503, title: "Database temporarily unavailable."); }
        catch (NpgsqlException) { return Results.Problem(statusCode: 503, title: "Database temporarily unavailable."); }
        catch (TimeoutException) { return Results.Problem(statusCode: 503, title: "Database temporarily unavailable."); }
    }
}
