using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using POS.Application.Common.Models;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

public class HealthCheckTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public HealthCheckTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ShouldReturn200OkAndOperationalStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Data.GetProperty("status").GetString().Should().Be("Healthy");
        content.Data.GetProperty("database").GetProperty("connected").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ping_ShouldReturnPong()
    {
        // Act
        var response = await _client.GetAsync("/api/health/ping");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Message.Should().Be("Pong");
    }

    [Fact]
    public async Task ReadyHealthCheck_ShouldReturn200Healthy()
    {
        // Act
        var response = await _client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Be("Healthy");
    }
}
