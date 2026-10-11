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

        // 4. Third terminal: reaches the 3 terminal limit for PRO tier
        var terminal3Uid = Guid.NewGuid();
        var thirdActivateResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal3Uid,
                CounterNumber: "03",
                CounterName: "Third Register"));

        thirdActivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Fourth terminal: exceeds the 3 terminal limit and must be rejected!
        var terminal4Uid = Guid.NewGuid();
        var fourthActivateResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal4Uid,
                CounterNumber: "04",
                CounterName: "Fourth Register"));

        fourthActivateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var fourthEnvelope = await fourthActivateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        fourthEnvelope!.Errors.Should().Contain(e => e.Contains("License limit reached"));

        // 6. Re-enrolling an existing terminal (terminal 1) rotates key without exceeding limit
        var reEnrollResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal1Uid,
                CounterNumber: "01",
                CounterName: "Front Desk Reinstalled"));

        reEnrollResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reEnrollEnvelope = await reEnrollResponse.Content.ReadFromJsonAsync<ApiResponse<ActivateStoreLicenseResponse>>(JsonOpts);
        reEnrollEnvelope!.Data!.TerminalUid.Should().Be(terminal1Uid);
        reEnrollEnvelope.Data.ApiKey.Should().NotBe(activateEnvelope.Data.ApiKey);

        // 7. Enrolling with different store details when key is already assigned does NOT replace or create store
        var anotherTerminalUid = Guid.NewGuid();
        var ignoreDetailsResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new ActivateStoreLicenseRequest(
                ProductKey: "MIQ-PRO-7K4D-92LX-AB83",
                TerminalUid: terminal2Uid, // re-registering terminal 2
                StoreName: "Completely Different Store Name",
                StoreCode: "DIFFERENT99",
                CounterNumber: "02",
                CounterName: "Register 2"));

        ignoreDetailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ignoreDetailsEnvelope = await ignoreDetailsResponse.Content.ReadFromJsonAsync<ApiResponse<ActivateStoreLicenseResponse>>(JsonOpts);
        ignoreDetailsEnvelope!.Data!.StoreId.Should().Be(storeId);
        ignoreDetailsEnvelope.Data.StoreCode.Should().Be(storeCode);
        ignoreDetailsEnvelope.Data.StoreName.Should().Be("Automated Test Store");
    }
}
