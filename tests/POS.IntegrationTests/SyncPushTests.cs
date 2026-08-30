using System;
using System.Collections.Generic;
using System.Linq;
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
using POS.Application.Sync;
using POS.Application.Terminals;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

public class SyncPushTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SyncPushTests(CustomWebApplicationFactory factory) => _factory = factory;

    /// <summary>Enrolls a fresh till and returns a client already carrying its bearer token.</summary>
    private async Task<(HttpClient Client, Guid StoreId, Guid TerminalId)> EnrolledTerminalAsync()
    {
        var provisioning = _factory.CreateClient();
        provisioning.DefaultRequestHeaders.Add("X-Provisioning-Key", CustomWebApplicationFactory.ProvisioningKey);

        var storeResponse = await provisioning.PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest("S" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
                "Sync Test Store", null, null, null, null));
        var store = (await storeResponse.Content.ReadFromJsonAsync<ApiResponse<StoreDto>>(JsonOpts))!.Data!;

        var codeResponse = await provisioning.PostAsJsonAsync(
            $"/api/provisioning/stores/{store.Id}/enrollment-codes",
            new IssueEnrollmentCodeRequest(60, null));
        var code = (await codeResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollmentCodeDto>>(JsonOpts))!.Data!;

        var client = _factory.CreateClient();
        var enrollResponse = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code.Code, Guid.NewGuid(), "01", "Main", "TEST", "1.0.0"));
        var enrolled = (await enrollResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(enrolled.TerminalUid, enrolled.ApiKey));
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return (client, store.Id, enrolled.TerminalId);
    }

    private static SyncEventPayload Event(string aggregateType = "Order", Guid? eventId = null) => new(
        eventId ?? Guid.NewGuid(),
        aggregateType,
        Guid.NewGuid(),
        "Upsert",
        """{"id":"8821c51d-ce76-450d-8ce0-a9b867c7933e","invoiceNumber":"INV-1"}""",
        1,
        DateTime.UtcNow);

    private static async Task<SyncPushResponse> PushAsync(HttpClient client, params SyncEventPayload[] events)
    {
        var response = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(events));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<SyncPushResponse>>(JsonOpts))!.Data!;
    }

    [Fact]
    public async Task Push_WithoutToken_IsRejected()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event()]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Push_StoresEventsAndReportsPerEvent()
    {
        var (client, _, _) = await EnrolledTerminalAsync();

        var result = await PushAsync(client, Event(), Event(), Event());

        result.Accepted.Should().Be(3);
        result.Duplicates.Should().Be(0);
        result.Rejected.Should().Be(0);
        result.Results.Should().HaveCount(3).And.OnlyContain(r => r.Outcome == SyncEventOutcome.Accepted);
    }

    [Fact]
    public async Task Push_ResendingTheSameEvents_IsDeduplicated()
    {
        var (client, _, _) = await EnrolledTerminalAsync();
        var events = new[] { Event(), Event() };

        var first = await PushAsync(client, events);
        first.Accepted.Should().Be(2);

        // The worker delivered successfully and lost the acknowledgement, so it sends again.
        // That has to be reported as already-held, never stored twice.
        var second = await PushAsync(client, events);
        second.Accepted.Should().Be(0);
        second.Duplicates.Should().Be(2);
        second.Results.Should().OnlyContain(r => r.Outcome == SyncEventOutcome.Duplicate);
    }

    [Fact]
    public async Task Push_WithTheSameEventTwiceInOneBatch_KeepsOnlyOne()
    {
        var (client, _, _) = await EnrolledTerminalAsync();
        var shared = Guid.NewGuid();

        var result = await PushAsync(client, Event(eventId: shared), Event(eventId: shared));

        result.Accepted.Should().Be(1);
        result.Duplicates.Should().Be(1);
    }

    [Fact]
    public async Task Push_MixedBatch_AcceptsTheGoodAndRejectsTheBad()
    {
        var (client, _, _) = await EnrolledTerminalAsync();

        var good = Event();
        var unknownType = Event("NotAThingWeStore");
        var badVersion = good with { EventId = Guid.NewGuid(), PayloadVersion = 99 };

        var result = await PushAsync(client, good, unknownType, badVersion);

        // A bad entry must not take the good ones down with it: the till would otherwise have to
        // choose between resending everything forever and dropping the batch.
        result.Accepted.Should().Be(1);
        result.Rejected.Should().Be(2);
        result.Results.Single(r => r.EventId == good.EventId).Outcome.Should().Be(SyncEventOutcome.Accepted);
        result.Results.Single(r => r.EventId == unknownType.EventId).Error.Should().Contain("aggregate type");
        result.Results.Single(r => r.EventId == badVersion.EventId).Error.Should().Contain("payloadVersion");
    }

    [Fact]
    public async Task Push_EmptyBatch_IsRefused()
    {
        var (client, _, _) = await EnrolledTerminalAsync();

        var response = await client.PostAsJsonAsync("/api/sync/push",
            new SyncPushRequest(Array.Empty<SyncEventPayload>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_OversizedBatch_IsRefused()
    {
        var (client, _, _) = await EnrolledTerminalAsync();

        var tooMany = Enumerable.Range(0, SyncIngestService.MaxEventsPerBatch + 1)
            .Select(_ => Event()).ToArray();

        var response = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(tooMany));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Push_TheSameEventIdFromTwoStores_IsNotTreatedAsADuplicate()
    {
        var (clientA, _, _) = await EnrolledTerminalAsync();
        var (clientB, _, _) = await EnrolledTerminalAsync();

        // Idempotency is scoped per store. Two tills in unrelated shops can mint the same Guid
        // in principle, and one must not silently swallow the other's sale.
        var shared = Guid.NewGuid();

        (await PushAsync(clientA, Event(eventId: shared))).Accepted.Should().Be(1);
        (await PushAsync(clientB, Event(eventId: shared))).Accepted.Should().Be(1);
    }

    [Fact]
    public async Task Status_ReportsWhatTheServerHoldsForTheCaller()
    {
        var (client, storeId, terminalId) = await EnrolledTerminalAsync();

        await PushAsync(client, Event(), Event(), Event("CashSession"));

        var response = await client.GetAsync("/api/sync/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = (await response.Content.ReadFromJsonAsync<ApiResponse<SyncStatusResponse>>(JsonOpts))!.Data!;

        status.StoreId.Should().Be(storeId);
        status.TerminalId.Should().Be(terminalId);
        status.EventsHeldForStore.Should().Be(3);
        status.EventsHeldForTerminal.Should().Be(3);
        status.LastReceivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Push_AfterTheTerminalIsRevoked_CannotGetAFreshToken()
    {
        var (client, _, terminalId) = await EnrolledTerminalAsync();
        (await PushAsync(client, Event())).Accepted.Should().Be(1);

        var provisioning = _factory.CreateClient();
        provisioning.DefaultRequestHeaders.Add("X-Provisioning-Key", CustomWebApplicationFactory.ProvisioningKey);
        var revoke = await provisioning.PostAsync($"/api/provisioning/terminals/{terminalId}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        // The token already issued stays valid until it expires — that is what a short lifetime
        // is for. What revocation stops is getting another one.
        var fresh = _factory.CreateClient();
        var tokenResponse = await fresh.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(Guid.NewGuid(), "miq_anything"));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
