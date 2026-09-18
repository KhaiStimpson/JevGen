using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace JevGen.IntegrationTests;

/// <summary>
/// Hosts the ASP.NET Core sample and exercises it over HTTP, confirming that an endpoint using
/// a JevGen contract needs no provider-specific code.
/// </summary>
public sealed class AspNetCoreTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AspNetCoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static readonly object Ticket = new { subject = "Billed twice", body = "Please refund one charge." };

    [Fact]
    public async Task AnEndpointReturnsATypedRoutingDecision()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets/route", Ticket);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Billing", root.GetProperty("department").GetString());
        Assert.Equal(0.94, root.GetProperty("confidence").GetDouble(), 6);
        Assert.Equal(3, root.GetProperty("probabilities").EnumerateObject().Count());
    }

    [Fact]
    public async Task AnAggregateEndpointAnswersEveryQuestion()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets/assess", Ticket);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(0.17, root.GetProperty("urgent").GetProperty("probability").GetDouble(), 6);
        Assert.Equal(2d, root.GetProperty("severity").GetProperty("value").GetDouble(), 6);
    }

    [Fact]
    public async Task ResultsDoNotLeakProviderDetailToApiCallers()
    {
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/tickets/assess", Ticket)).Content.ReadAsStringAsync();

        // Provider names, models and request ids are operational detail. Returning a result type
        // straight from an endpoint must not publish them.
        Assert.DoesNotContain("provider", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheConfidenceGateAllowsAConfidentAnswer()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets/route-confident", Ticket);

        // The sample answers at 94%, comfortably above the endpoint's 90% floor.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheContractCanBeInspectedWithoutCredentials()
    {
        using var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/tickets/contract");

        Assert.Contains("\"ITicketAI\"", body, StringComparison.Ordinal);
        Assert.Contains("\"route\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthChecksRespond()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
