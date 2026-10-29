using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Financial.Api.Tests;
public sealed class ApiValidationTests
{
    [Fact] public async Task SwaggerListsSecurityAndIdempotencyHeader()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(json.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal("string", schemas.GetProperty("MovementType").GetProperty("type").GetString());
        Assert.Equal(new[] { "Credit", "Debit" }, schemas.GetProperty("MovementType").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains(schemas.GetProperty("MovementRequest").GetProperty("required").EnumerateArray(), e => e.GetString() == "amount");
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/customers", out _));
        var operation = paths.GetProperty("/api/accounts/{accountId}/movements").GetProperty("post");
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
    }
    [Theory] [InlineData("", 401)] [InlineData("Bearer invalid", 401)]
    public async Task InvalidAuthenticationCannotRead(string authorization, int expected)
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        if (authorization.Length > 0) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        var response = await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/balance");
        Assert.Equal(expected, (int)response.StatusCode);
    }
    [Fact] public async Task ExpiredTokenIsRejected()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(expired: true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/balance")).StatusCode);
    }
    [Fact] public async Task OperatorCannotRegisterCustomers()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/customers", new { name = "Name", subjectId = "owner" })).StatusCode);
    }
    [Fact] public async Task AdministratorWithInvalidCustomerReceives400()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(role: "admin"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", new { name = "", subjectId = "owner" })).StatusCode);
    }
    [Theory] [InlineData("balance?at=2099-01-01T00:00:00Z")] [InlineData("balance?at=2026-01-01T00:00:00")] [InlineData("movements?limit=101")] [InlineData("movements?cursor=bad")]
    public async Task InvalidQueryReceives400BeforeDatabaseAccess(string query)
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/{query}")).StatusCode);
    }
    [Fact] public async Task DatabaseFailureIs503WithoutInternalDetails()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var response = await client.PostAsJsonAsync("/api/customers", new { name = "Valid", subjectId = "owner" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        response = await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/balance");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("Npgsql", text); Assert.DoesNotContain("Password", text);
    }
    [Fact] public async Task MissingMovementTypeReceives400()
    {
        await using var factory = new ApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "missing-type");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/accounts/{Guid.NewGuid()}/movements", new { amount = 10m })).StatusCode);
    }
}
