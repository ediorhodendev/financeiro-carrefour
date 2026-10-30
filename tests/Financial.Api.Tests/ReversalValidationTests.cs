using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Financial.Api.Tests;
public sealed class ReversalValidationTests
{
    private static readonly string Path = $"/api/accounts/{Guid.NewGuid()}/movements/{Guid.NewGuid()}/reversal";
    [Fact] public async Task ReversalRequiresAuthentication()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Path, new { reason = "Correction" })).StatusCode);
    }
    [Fact] public async Task AdminAloneCannotReverse()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("admin", "admin"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Path, new { reason = "Correction" })).StatusCode);
    }
    [Fact] public async Task ReversalRequiresIdempotencyKey()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new { reason = "Correction" })).StatusCode);
    }
    [Theory] [InlineData("")] [InlineData("   ")] [InlineData(null)]
    public async Task ReversalRejectsBlankReason(string? reason)
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "reversal-validation");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new { reason })).StatusCode);
    }
    [Fact] public async Task ClientCannotSupplyPartialReversalAmount()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "partial-reversal");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new { reason = "Correction", amount = 1m })).StatusCode);
    }
    [Fact] public async Task SwaggerDocumentsRequiredHeaderAndReason()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/accounts/{accountId}/movements/{movementId}/reversal").GetProperty("post");
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key" && p.GetProperty("required").GetBoolean());
        Assert.Contains(document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ReversalRequest").GetProperty("required").EnumerateArray(), r => r.GetString() == "reason");
    }
}
