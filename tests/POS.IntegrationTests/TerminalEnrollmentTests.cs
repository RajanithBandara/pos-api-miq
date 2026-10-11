using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Common.Models;
using POS.Application.Licensing;
using POS.Application.Provisioning;
using POS.Application.Terminals;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

public class TerminalEnrollmentTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TerminalEnrollmentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private HttpClient CreateProvisioningClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Provisioning-Key", CustomWebApplicationFactory.ProvisioningKey);
        return client;
    }

    /// <summary>Activates a terminal using a product key and returns its details.</summary>
    private async Task<(Guid StoreId, Guid TerminalId, Guid TerminalUid, string ApiKey)> ActivateTerminalAsync(string? key = null)
    {
        var client = CreateClient();
        key ??= "MIQ-STD-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<POS.Domain.Interfaces.IProductKeyRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<POS.Domain.Interfaces.IUnitOfWork>();
            await keys.AddAsync(POS.Domain.Entities.ProductKey.Create(key, "PRO", maxTerminals: 5));
            await uow.SaveChangesAsync();
        }

        var storeCode = "S" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var terminalUid = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: key,
                TerminalUid: terminalUid,
                StoreName: "Test Store",
                StoreCode: storeCode,
                CounterNumber: "01",
                CounterName: "Main Counter",
                MachineName: "TEST-PC",
                AppVersion: "1.0.0"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var activated = (await response.Content.ReadFromJsonAsync<ApiResponse<ActivateStoreLicenseResponse>>(JsonOpts))!.Data!;
        return (activated.StoreId, activated.TerminalId, activated.TerminalUid, activated.ApiKey);
    }

    [Fact]
    public async Task Provisioning_WithoutKey_IsRejected()
    {
        var response = await CreateClient().GetAsync("/api/provisioning/stores");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provisioning_WithWrongKey_IsRejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Provisioning-Key", "not-the-key");

        var response = await client.GetAsync("/api/provisioning/stores");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateStoreCode_IsRejected()
    {
        var provisioning = CreateProvisioningClient();
        var code = "DUPE" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        var first = await provisioning.PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest(code, "First", null, null, null, null));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await provisioning.PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest(code, "Second", null, null, null, null));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Token_WithCorrectKey_IssuesBearerToken()
    {
        var (storeId, _, terminalUid, apiKey) = await ActivateTerminalAsync();
        var client = CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, apiKey));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        token.AccessToken.Should().NotBeNullOrWhiteSpace();
        token.TokenType.Should().Be("Bearer");
        token.StoreId.Should().Be(storeId);
        token.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Token_WithWrongKey_IsRejected()
    {
        var (_, _, terminalUid, _) = await ActivateTerminalAsync();
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, "miq_definitely-not-the-real-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ForUnknownTerminal_AnswersTheSameAsAWrongKey()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(Guid.NewGuid(), "miq_whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        body!.Errors.Should().ContainSingle().Which.Should().Be("Those terminal credentials are not valid.");
    }

    [Fact]
    public async Task Me_WithoutToken_IsRejected()
    {
        var response = await CreateClient().GetAsync("/api/terminals/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithTerminalToken_ReturnsItsOwnRecord()
    {
        var (storeId, _, terminalUid, apiKey) = await ActivateTerminalAsync();
        var client = CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, apiKey));
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await client.GetAsync("/api/terminals/me");

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<ApiResponse<TerminalDto>>(JsonOpts);

        body!.Data!.TerminalUid.Should().Be(terminalUid);
        body.Data.StoreId.Should().Be(storeId);
        body.Data.CounterNumber.Should().Be("01");
        body.Data.CounterName.Should().Be("Main Counter");
    }

    [Fact]
    public async Task RevokedTerminal_CannotGetANewToken()
    {
        var (_, terminalId, terminalUid, apiKey) = await ActivateTerminalAsync();
        var client = CreateClient();

        var revoke = await CreateProvisioningClient()
            .PostAsync($"/api/provisioning/terminals/{terminalId}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, apiKey));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuspendedTerminal_CannotGetANewToken_ButCanBeReactivated()
    {
        var (_, terminalId, terminalUid, apiKey) = await ActivateTerminalAsync();
        var client = CreateClient();
        var provisioning = CreateProvisioningClient();

        await provisioning.PostAsync($"/api/provisioning/terminals/{terminalId}/suspend", null);

        var whileSuspended = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, apiKey));
        whileSuspended.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Suspension keeps the credential, so reactivating restores access without re-enrolling.
        await provisioning.PostAsync($"/api/provisioning/terminals/{terminalId}/reactivate", null);

        var afterReactivation = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, apiKey));
        afterReactivation.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
