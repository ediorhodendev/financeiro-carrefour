using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Financial.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Financial.Api.Development;

// Explicitly enabled by the local evaluation Compose only. Never a production identity provider.
public sealed class DemoAccess
{
    private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));
    private const string Issuer = "financial-docker-demo";
    public static DemoAccess? Create(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Demo:Enabled")) return null;
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Demo access requires Development.");
        return new DemoAccess();
    }

    public void Configure(JwtBearerOptions options)
    {
        options.Authority = null;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Issuer,
            ValidateAudience = true, ValidAudience = Issuer,
            ValidateIssuerSigningKey = true, IssuerSigningKey = key,
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(5), RoleClaimType = "role"
        };
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/demo/access", async (FinancialDbContext db, CancellationToken ct) =>
        {
            var accountId = await db.Accounts.Where(a => db.Customers.Any(c =>
                c.Id == a.CustomerId && c.SubjectId == "demo-operator"))
                .Select(a => a.Id).SingleAsync(ct);
            return Results.Ok(new
            {
                accountId,
                operatorToken = Token("demo-operator", "operator"),
                adminToken = Token("demo-admin", "admin"),
                expiresInSeconds = 3600
            });
        }).WithTags("Local demo").WithSummary("Local evaluation only: get fictitious account and temporary JWTs.");
    }

    private string Token(string subject, string role)
    {
        var token = new JwtSecurityToken(Issuer, Issuer,
            [new Claim("sub", subject), new Claim("role", role)],
            notBefore: DateTime.UtcNow.AddSeconds(-1), expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
