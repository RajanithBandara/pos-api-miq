using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Common.Models;
using POS.Application.Provisioning;
using POS.Application.Sync;
using POS.Application.Terminals;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

/// <summary>
/// Guards the feed's ordering property directly.
///
/// This exists because an earlier design took the sequence from a database identity column, and
/// every one of these assertions passed while the number was silently always zero — the events
/// were stored, the pushes succeeded, and the feed was simply empty forever. Nothing else in the
/// suite noticed, because nothing else looked at the sequence itself.
/// </summary>
public class SyncSequenceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SyncSequenceTests(CustomWebApplicationFactory factory) => _factory = factory;

    private readonly System.Collections.Generic.Dictionary<Guid, string> _storeKeys = new();

    private async Task<(HttpClient Client, Guid StoreId)> EnrolledAsync(Guid? existingStore = null)
    {
        var client = _factory.CreateClient();
        string productKey;
        Guid storeId;

        if (existingStore is Guid known)
        {
            storeId = known;
            productKey = _storeKeys[known];
        }
        else
        {
            productKey = "MIQ-STD-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            using (var scope = _factory.Services.CreateScope())
            {
                var keys = scope.ServiceProvider.GetRequiredService<POS.Domain.Interfaces.IProductKeyRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<POS.Domain.Interfaces.IUnitOfWork>();
                await keys.AddAsync(POS.Domain.Entities.ProductKey.Create(productKey, "PRO", maxTerminals: 10));
                await uow.SaveChangesAsync();
            }

            var storeCode = "Q" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
            var initResp = await client.PostAsJsonAsync("/api/licensing/activate",
                new POS.Application.Licensing.ActivateStoreLicenseRequest(
                    ProductKey: productKey,
                    TerminalUid: Guid.NewGuid(),
                    StoreName: "Sequence Store",
                    StoreCode: storeCode,
                    CounterNumber: "00",
                    CounterName: "Setup"));
            var initData = (await initResp.Content.ReadFromJsonAsync<ApiResponse<POS.Application.Licensing.ActivateStoreLicenseResponse>>(JsonOpts))!.Data!;
            storeId = initData.StoreId;
            _storeKeys[storeId] = productKey;
        }

        var terminalUid = Guid.NewGuid();
        var activateResponse = await client.PostAsJsonAsync("/api/licensing/activate",
            new POS.Application.Licensing.ActivateStoreLicenseRequest(
                ProductKey: productKey,
                TerminalUid: terminalUid,
                CounterNumber: "01",
                CounterName: "Counter"));
        var activated = (await activateResponse.Content.ReadFromJsonAsync<ApiResponse<POS.Application.Licensing.ActivateStoreLicenseResponse>>(JsonOpts))!.Data!;

        var tokenResponse = await client.PostAsJsonAsync("/api/terminals/token",
            new TerminalTokenRequest(activated.TerminalUid, activated.ApiKey));
        var token = (await tokenResponse.Content.ReadFromJsonAsync<ApiResponse<TerminalTokenResponse>>(JsonOpts))!.Data!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, storeId);
    }

    private static SyncEventPayload Event() => new(
        Guid.NewGuid(), "Order", Guid.NewGuid(), "Upsert", """{"id":"x"}""", 1, DateTime.UtcNow);

    [Fact]
    public async Task Sequences_AreAssignedStartingAtOne()
    {
        var (client, _) = await EnrolledAsync();

        await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event(), Event()]));

        var status = (await (await client.GetAsync("/api/sync/status"))
            .Content.ReadFromJsonAsync<ApiResponse<SyncStatusResponse>>(JsonOpts))!.Data!;

        status.EventsHeldForStore.Should().Be(2);
        status.HighestSequence.Should().Be(2, "the feed is unusable if the sequence is never assigned");
    }

    [Fact]
    public async Task Sequences_AreGaplessAndContinueAcrossPushesAndTerminals()
    {
        var (counterOne, storeId) = await EnrolledAsync();
        var (counterTwo, _) = await EnrolledAsync(storeId);

        await counterOne.PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event(), Event(), Event()]));
        await counterTwo.PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event(), Event()]));

        var page = (await (await counterOne.GetAsync("/api/sync/pull?since=0&limit=100"))
            .Content.ReadFromJsonAsync<ApiResponse<SyncPullResponse>>(JsonOpts))!.Data!;

        // Counter one sees only counter two's pair, but the numbering runs 1..5 across the store
        // as a whole, so the cursor cannot land between two events and lose one.
        page.NextCursor.Should().Be(5);
        page.Events.Select(e => e.Sequence).Should().Equal(4, 5);
    }

    [Fact]
    public async Task Sequences_AreScopedPerStore()
    {
        var (storeA, _) = await EnrolledAsync();
        var (storeB, _) = await EnrolledAsync();

        await storeA.PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event(), Event(), Event()]));
        await storeB.PostAsJsonAsync("/api/sync/push", new SyncPushRequest([Event()]));

        var statusA = (await (await storeA.GetAsync("/api/sync/status"))
            .Content.ReadFromJsonAsync<ApiResponse<SyncStatusResponse>>(JsonOpts))!.Data!;
        var statusB = (await (await storeB.GetAsync("/api/sync/status"))
            .Content.ReadFromJsonAsync<ApiResponse<SyncStatusResponse>>(JsonOpts))!.Data!;

        // Each store counts from one. A global counter would leave every store's feed full of
        // holes belonging to other shops, and a cursor could not tell a hole from a gap.
        statusA.HighestSequence.Should().Be(3);
        statusB.HighestSequence.Should().Be(1);
    }
}
