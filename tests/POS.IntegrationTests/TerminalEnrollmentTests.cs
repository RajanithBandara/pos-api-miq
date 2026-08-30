using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using POS.Application.Common.Models;
using POS.Application.Provisioning;
using POS.Application.Terminals;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

public class TerminalEnrollmentTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    // Mirrors the API's own serializer: enums go over the wire as names, not ordinals, so the
    // client has to read them the same way.
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

    /// <summary>Creates a store and mints a code, the way an installer would be set up.</summary>
    private async Task<(Guid StoreId, string Code)> ProvisionStoreWithCodeAsync(string? storeCode = null)
    {
        var provisioning = CreateProvisioningClient();

        var storeResponse = await provisioning.PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest(
                storeCode ?? "ST" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                "Test Store", null, null, null, null));

        storeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var store = await storeResponse.Content.ReadFromJsonAsync<ApiResponse<StoreDto>>(JsonOpts);
        var storeId = store!.Data!.Id;

        var codeResponse = await provisioning.PostAsJsonAsync(
            $"/api/provisioning/stores/{storeId}/enrollment-codes",
            new IssueEnrollmentCodeRequest(60, "integration test"));

        codeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var code = await codeResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollmentCodeDto>>(JsonOpts);

        return (storeId, code!.Data!.Code);
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
    public async Task Enroll_WithValidCode_ReturnsApiKeyAndStore()
    {
        var (storeId, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();

        var response = await CreateClient().PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "01", "Main Counter", "TEST-PC", "1.0.0"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts);
        body!.Success.Should().BeTrue();
        body.Data!.StoreId.Should().Be(storeId);
        body.Data.TerminalUid.Should().Be(terminalUid);
        body.Data.ApiKey.Should().StartWith("miq_");
        body.Data.ApiKey.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public async Task Enroll_WithUnknownCode_IsRejectedWithoutRevealingAnything()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest("ZZZZ-ZZZZ", Guid.NewGuid(), "01", "Main", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        body!.Errors.Should().ContainSingle().Which.Should().Be("That enrollment code is not valid.");
    }

    [Fact]
    public async Task Enroll_ReusingACode_IsRejected()
    {
        var (_, code) = await ProvisionStoreWithCodeAsync();
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, Guid.NewGuid(), "01", "Main", null, null));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Single use: the same code must not enroll a second till.
        var second = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, Guid.NewGuid(), "02", "Second", null, null));

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enroll_SameTerminalIntoAnotherStore_IsRejected()
    {
        var (_, firstCode) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();

        var client = CreateClient();
        var enrolled = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(firstCode, terminalUid, "01", "Main", null, null));
        enrolled.StatusCode.Should().Be(HttpStatusCode.OK);

        // The till's unsent events belong to the first store, so moving it is refused.
        var (_, otherCode) = await ProvisionStoreWithCodeAsync();
        var moved = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(otherCode, terminalUid, "01", "Main", null, null));

        moved.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Enroll_SameTerminalAndStoreAgain_RotatesTheKey()
    {
        var (_, firstCode) = await ProvisionStoreWithCodeAsync("REINSTALL");
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(firstCode, terminalUid, "01", "Main", null, null));
        var firstBody = await first.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts);

        // A reinstall of the same machine: fresh code, same terminal identity.
        var provisioning = CreateProvisioningClient();
        var codeResponse = await provisioning.PostAsJsonAsync(
            $"/api/provisioning/stores/{firstBody!.Data!.StoreId}/enrollment-codes",
            new IssueEnrollmentCodeRequest(60, "reinstall"));
        var secondCode = (await codeResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollmentCodeDto>>(JsonOpts))!.Data!.Code;

        var second = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(secondCode, terminalUid, "01", "Main", null, null));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts);

        // Same terminal, new secret — history is preserved, the credential is not.
        secondBody!.Data!.TerminalId.Should().Be(firstBody.Data.TerminalId);
        secondBody.Data.ApiKey.Should().NotBe(firstBody.Data.ApiKey);
    }

    [Fact]
    public async Task Token_WithCorrectKey_IssuesBearerToken()
    {
        var (storeId, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();

        var enroll = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "01", "Main", null, null));
        var enrolled = (await enroll.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, enrolled.ApiKey));

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
        var (_, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();

        await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "01", "Main", null, null));

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
        var (storeId, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();

        var enroll = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "07", "Express Lane", "TILL-07", "1.2.3"));
        var enrolled = (await enroll.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, enrolled.ApiKey));
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await client.GetAsync("/api/terminals/me");

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<ApiResponse<TerminalDto>>(JsonOpts);

        body!.Data!.TerminalUid.Should().Be(terminalUid);
        body.Data.StoreId.Should().Be(storeId);
        body.Data.CounterNumber.Should().Be("07");
        body.Data.CounterName.Should().Be("Express Lane");
    }

    [Fact]
    public async Task RevokedTerminal_CannotGetANewToken()
    {
        var (_, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();

        var enroll = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "01", "Main", null, null));
        var enrolled = (await enroll.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        var revoke = await CreateProvisioningClient()
            .PostAsync($"/api/provisioning/terminals/{enrolled.TerminalId}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, enrolled.ApiKey));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuspendedTerminal_CannotGetANewToken_ButCanBeReactivated()
    {
        var (_, code) = await ProvisionStoreWithCodeAsync();
        var terminalUid = Guid.NewGuid();
        var client = CreateClient();
        var provisioning = CreateProvisioningClient();

        var enroll = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code, terminalUid, "01", "Main", null, null));
        var enrolled = (await enroll.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        await provisioning.PostAsync($"/api/provisioning/terminals/{enrolled.TerminalId}/suspend", null);

        var whileSuspended = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, enrolled.ApiKey));
        whileSuspended.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Suspension keeps the credential, so reactivating restores access without re-enrolling.
        await provisioning.PostAsync($"/api/provisioning/terminals/{enrolled.TerminalId}/reactivate", null);

        var afterReactivation = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(terminalUid, enrolled.ApiKey));
        afterReactivation.StatusCode.Should().Be(HttpStatusCode.OK);
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
}
