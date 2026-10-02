using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using POS.Application.Common.Models;
using POS.Application.Licensing;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

public class LicensingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LicensingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task ValidateKey_ReturnsSuccess_ForPreSeededDefaultKey()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/licensing/validate-key",
            new ValidateProductKeyRequest("MIQ-PRO-7K4D-92LX-AB83"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ValidateProductKeyResponse>>(JsonOpts);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Valid.Should().BeTrue();
        envelope.Data.KeyCode.Should().Be("MIQ-PRO-7K4D-92LX-AB83");
        envelope.Data.PlanTier.Should().Be("PRO");
        envelope.Data.MaxTerminals.Should().Be(3);
    }

    [Fact]
    public async Task ValidateKey_ReturnsBadRequest_ForUnknownKey()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/licensing/validate-key",
            new ValidateProductKeyRequest("MIQ-FAKE-0000-0000-0000"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Activate_CreatesStore_AndEnrollsAdditionalTerminal_UnderLicenseLimit()
    {
        var client = CreateClient();

        // 1. Initial activation on default key
        var terminal1Uid = Guid.NewGuid();
        var storeCode = "ST" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        var activateResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal1Uid,
                StoreName: "Automated Test Store",
                StoreCode: storeCode,
                CounterNumber: "01",
                CounterName: "Front Desk"));

        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activateEnvelope = await activateResponse.Content.ReadFromJsonAsync<ApiResponse<ActivateStoreLicenseResponse>>(JsonOpts);
        activateEnvelope.Should().NotBeNull();
        activateEnvelope!.Data.Should().NotBeNull();
        activateEnvelope.Data!.StoreCode.Should().Be(storeCode);
        activateEnvelope.Data.ApiKey.Should().NotBeNullOrWhiteSpace();
        var storeId = activateEnvelope.Data.StoreId;

        // 2. Validate key again — should now recognize assigned store
        var validateResponse = await client.PostAsJsonAsync("/api/licensing/validate-key",
            new ValidateProductKeyRequest("MIQ-PRO-7K4D-92LX-AB83"));

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var validateEnvelope = await validateResponse.Content.ReadFromJsonAsync<ApiResponse<ValidateProductKeyResponse>>(JsonOpts);
        validateEnvelope!.Data!.IsStoreAssigned.Should().BeTrue();
        validateEnvelope.Data.StoreId.Should().Be(storeId);
        validateEnvelope.Data.EnrolledTerminalsCount.Should().Be(1);

        // 3. Multi-terminal support: Second terminal activates using the same key!
        var terminal2Uid = Guid.NewGuid();
        var secondActivateResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal2Uid,
                CounterNumber: "02",
                CounterName: "Second Register"));

        secondActivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondEnvelope = await secondActivateResponse.Content.ReadFromJsonAsync<ApiResponse<ActivateStoreLicenseResponse>>(JsonOpts);
        secondEnvelope!.Data!.StoreId.Should().Be(storeId);
        secondEnvelope.Data.TerminalUid.Should().Be(terminal2Uid);
        secondEnvelope.Data.ApiKey.Should().NotBeNullOrWhiteSpace();
    }
}
