using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Financial.Application.Provisioning;
using Financial.Application.Movements;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Financial.Api.Tests;
public sealed class ApiPostgresTests
{
    [Fact] [Trait("Category", "Postgres")]
    public async Task FullFlowCreatesAccountAndProtectsFinancialOperations()
    {
        await using var factory = new ApiFactory(postgres: true); using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        { await scope.ServiceProvider.GetRequiredService<FinancialDbContext>().Database.MigrateAsync(); }
        var subject = "http-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("administrator", "admin"));
        var response = await client.PostAsJsonAsync("/api/customers", new { name = "HTTP Customer", subjectId = subject });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = (await response.Content.ReadFromJsonAsync<CustomerResult>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/customers", new { name = "Duplicate", subjectId = subject })).StatusCode);
        response = await client.PostAsync($"/api/customers/{customer.Id}/accounts", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = (await response.Content.ReadFromJsonAsync<AccountResult>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/customers/{customer.Id}/accounts", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(subject));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-credit");
        var movementUri = $"/api/accounts/{account.Id}/movements";
        response = await client.PostAsJsonAsync(movementUri, new { type = "Credit", amount = 100m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var first = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var firstId = first.RootElement.GetProperty("id").GetGuid();
        response = await client.PostAsJsonAsync(movementUri, new { type = "Credit", amount = 100m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var replay = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(firstId, replay.RootElement.GetProperty("id").GetGuid()); Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "http-debit");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(movementUri, new { type = "Debit", amount = 30m })).StatusCode);
        using var balance = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/accounts/{account.Id}/balance"));
        Assert.Equal(70m, balance.RootElement.GetProperty("balance").GetDecimal());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "http-reversal");
        var reversalUri = movementUri + $"/{firstId}/reversal";
        // Only 70 remain, so reversing the original 100 credit is rejected.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reversalUri, new { reason = "Mistaken credit" })).StatusCode);
        // Locate and reverse the debit instead, restoring the balance to 100.
        using var statement = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(movementUri));
        var debitId = statement.RootElement.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("type").GetString() == "Debit").GetProperty("id").GetGuid();
        reversalUri = movementUri + $"/{debitId}/reversal";
        response = await client.PostAsJsonAsync(reversalUri, new { reason = "Mistaken debit" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var reversal = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(debitId, reversal.RootElement.GetProperty("reversalOfMovementId").GetGuid());
        var reversalId = reversal.RootElement.GetProperty("id").GetGuid();
        response = await client.PostAsJsonAsync(reversalUri, new { reason = "Mistaken debit" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reversedReplay = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(reversalId, reversedReplay.RootElement.GetProperty("id").GetGuid());
        Assert.True(reversedReplay.RootElement.GetProperty("replayed").GetBoolean());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", "another-reversal");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reversalUri, new { reason = "Again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(movementUri + $"/{reversalId}/reversal", new { reason = "Undo reversal" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(movementUri + $"/{Guid.NewGuid()}/reversal", new { reason = "Missing" })).StatusCode);
        using var restored = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/accounts/{account.Id}/balance"));
        Assert.Equal(100m, restored.RootElement.GetProperty("balance").GetDecimal());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("other-owner"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{account.Id}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(reversalUri, new { reason = "Wrong owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(movementUri, new { type = "Credit", amount = 5m })).StatusCode);
    }
}
