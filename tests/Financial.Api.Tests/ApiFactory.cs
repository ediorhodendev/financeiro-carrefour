using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
namespace Financial.Api.Tests;
public sealed class ApiFactory(bool postgres = false) : WebApplicationFactory<Program>
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var connection = postgres
            ? Environment.GetEnvironmentVariable("FINANCIAL_TEST_CONNECTION") ?? throw new InvalidOperationException("Set FINANCIAL_TEST_CONNECTION to an isolated PostgreSQL test database.")
            : "Host=127.0.0.1;Port=1;Database=not_available;Username=test;Password=test;Timeout=1;Pooling=false";
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Financial"] = connection }));
        builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "financial-tests", ValidateAudience = true, ValidAudience = "financial-tests",
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero, RoleClaimType = "role"
            };
        }));
    }
    public string Token(string subject = "test-subject", string? role = "operator", bool expired = false)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (role is not null) claims.Add(new("role", role));
        var token = new JwtSecurityToken("financial-tests", "financial-tests", claims,
            notBefore: DateTime.UtcNow.AddHours(-2), expires: expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
