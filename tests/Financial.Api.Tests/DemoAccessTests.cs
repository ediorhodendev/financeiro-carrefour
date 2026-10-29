using Financial.Api.Development;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace Financial.Api.Tests;
public sealed class DemoAccessTests
{
    [Fact] public async Task DemoEndpointIsAbsentByDefault()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await client.PostAsync("/demo/access", null)).StatusCode);
    }
    [Fact] public void DemoCannotBeEnabledInProduction()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Demo:Enabled"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => DemoAccess.Create(configuration, new ProductionEnvironment()));
    }
    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
