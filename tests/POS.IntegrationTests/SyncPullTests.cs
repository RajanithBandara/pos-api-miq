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

public class SyncPullTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SyncPullTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient Provisioning()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Provisioning-Key", CustomWebApplicationFactory.ProvisioningKey);
        return client;
    }

    private async Task<Guid> CreateStoreAsync()
    {
        var response = await Provisioning().PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest("P" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
                "Pull Test Store", null, null, null, null));

        return (await response.Content.ReadFromJsonAsync<ApiResponse<StoreDto>>(JsonOpts))!.Data!.Id;
    }

    /// <summary>Enrolls another till into an existing store, so peers share a feed.</summary>
    private async Task<HttpClient> EnrollIntoAsync(Guid storeId)
    {
        var codeResponse = await Provisioning().PostAsJsonAsync(
            $"/api/provisioning/stores/{storeId}/enrollment-codes",
            new IssueEnrollmentCodeRequest(60, null));
        var code = (await codeResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollmentCodeDto>>(JsonOpts))!.Data!;

        var client = _factory.CreateClient();
        var enrollResponse = await client.PostAsJsonAsync("/api/terminals/enroll",
            new EnrollTerminalRequest(code.Code, Guid.NewGuid(), "01", "Counter", "TEST", "1.0.0"));
        var enrolled = (await enrollResponse.Content.ReadFromJsonAsync<ApiResponse<EnrollTerminalResponse>>(JsonOpts))!.Data!;

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(enrolled.TerminalUid, enrolled.ApiKey));
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static SyncEventPayload Event(string aggregateType = "Order") => new(
        Guid.NewGuid(), aggregateType, Guid.NewGuid(), "Upsert",
        """{"id":"8821c51d-ce76-450d-8ce0-a9b867c7933e"}""", 1, DateTime.UtcNow);

    private static async Task PushAsync(HttpClient client, params SyncEventPayload[] events)
    {
        var response = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(events));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<SyncPullResponse> PullAsync(HttpClient client, long since = 0, int? limit = null)
    {
        var url = $"/api/sync/pull?since={since}" + (limit is null ? "" : $"&limit={limit}");
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<SyncPullResponse>>(JsonOpts))!.Data!;
    }

    [Fact]
    public async Task Pull_WithoutToken_IsRejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/sync/pull?since=0");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pull_ReturnsWhatAPeerSentAndNotItsOwn()
    {
        var storeId = await CreateStoreAsync();
        var counterOne = await EnrollIntoAsync(storeId);
        var counterTwo = await EnrollIntoAsync(storeId);

        await PushAsync(counterOne, Event(), Event());
        await PushAsync(counterTwo, Event("CashSession"));

        var seenByTwo = await PullAsync(counterTwo);

        // Counter two gets counter one's two sales, and is not handed back its own event —
        // applying that would create a second copy of a sale it already holds.
        seenByTwo.Events.Should().HaveCount(2);
        seenByTwo.Events.Should().OnlyContain(e => e.AggregateType == "Order");
    }

    [Fact]
    public async Task Pull_CursorAdvancesPastTheCallersOwnEvents()
    {
        var storeId = await CreateStoreAsync();
        var counterOne = await EnrollIntoAsync(storeId);
        var counterTwo = await EnrollIntoAsync(storeId);

        await PushAsync(counterOne, Event(), Event(), Event(), Event(), Event());

        // Counter one asks what it missed. Everything in the store so far is its own, so the
        // page is empty — but the cursor must still move past them. If it tracked only what was
        // returned, a busy till would rescan its whole history on every cycle forever.
        var page = await PullAsync(counterOne);

        page.Events.Should().BeEmpty();
        page.NextCursor.Should().BeGreaterThan(0);

        await PushAsync(counterTwo, Event("Customer"));

        var next = await PullAsync(counterOne, page.NextCursor);
        next.Events.Should().ContainSingle().Which.AggregateType.Should().Be("Customer");
    }

    [Fact]
    public async Task Pull_PagesThroughAndReportsWhenMoreRemains()
    {
        var storeId = await CreateStoreAsync();
        var counterOne = await EnrollIntoAsync(storeId);
        var counterTwo = await EnrollIntoAsync(storeId);

        await PushAsync(counterOne, Enumerable.Range(0, 25).Select(_ => Event()).ToArray());

        var first = await PullAsync(counterTwo, since: 0, limit: 10);
        first.Events.Should().HaveCount(10);
        first.HasMore.Should().BeTrue();

        var second = await PullAsync(counterTwo, first.NextCursor, limit: 10);
        second.Events.Should().HaveCount(10);
        second.HasMore.Should().BeTrue();

        var third = await PullAsync(counterTwo, second.NextCursor, limit: 10);
        third.Events.Should().HaveCount(5);
        third.HasMore.Should().BeFalse();

        // Every event arrived exactly once across the three pages.
        var all = first.Events.Concat(second.Events).Concat(third.Events).Select(e => e.EventId).ToList();
        all.Should().OnlyHaveUniqueItems().And.HaveCount(25);
    }

    [Fact]
    public async Task Pull_NeverCrossesStoreBoundaries()
    {
        var storeA = await CreateStoreAsync();
        var storeB = await CreateStoreAsync();

        var tillInA = await EnrollIntoAsync(storeA);
        var peerInA = await EnrollIntoAsync(storeA);
        var tillInB = await EnrollIntoAsync(storeB);

        await PushAsync(peerInA, Event(), Event());
        await PushAsync(tillInB, Event("ReceiptRefund"), Event("ReceiptRefund"), Event("ReceiptRefund"));

        var seenInA = await PullAsync(tillInA);

        // A till only ever sees its own store. The store comes from the token, so this cannot be
        // influenced by anything the caller sends.
        seenInA.Events.Should().HaveCount(2);
        seenInA.Events.Should().OnlyContain(e => e.AggregateType == "Order");
    }

    [Fact]
    public async Task Pull_RepeatedWithTheSameCursor_IsStable()
    {
        var storeId = await CreateStoreAsync();
        var counterOne = await EnrollIntoAsync(storeId);
        var counterTwo = await EnrollIntoAsync(storeId);

        await PushAsync(counterOne, Event(), Event());

        // A till that crashed before recording its new cursor asks again from the old one. It
        // must be handed exactly the same page, because at-least-once is the delivery contract.
        var first = await PullAsync(counterTwo, since: 0);
        var again = await PullAsync(counterTwo, since: 0);

        again.Events.Select(e => e.EventId).Should().Equal(first.Events.Select(e => e.EventId));
        again.NextCursor.Should().Be(first.NextCursor);
    }

    [Fact]
    public async Task Pull_WithANegativeCursor_IsRefused()
    {
        var storeId = await CreateStoreAsync();
        var client = await EnrollIntoAsync(storeId);

        var response = await client.GetAsync("/api/sync/pull?since=-5");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pull_CarriesTheOriginTerminalSoAStoreViewKnowsWhereASaleWasRung()
    {
        var storeId = await CreateStoreAsync();
        var counterOne = await EnrollIntoAsync(storeId);
        var counterTwo = await EnrollIntoAsync(storeId);

        await PushAsync(counterOne, Event());

        var page = await PullAsync(counterTwo);

        page.Events.Should().ContainSingle();
        page.Events[0].TerminalId.Should().NotBeEmpty();
        page.Events[0].Sequence.Should().BeGreaterThan(0);
    }
}
