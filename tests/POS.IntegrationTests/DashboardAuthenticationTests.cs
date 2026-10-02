using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using POS.Application.Common.Models;
using POS.Application.Dashboard;
using POS.Application.Provisioning;
using POS.Application.Sync;
using POS.Application.Terminals;
using POS.Domain.Enums;
using POS.IntegrationTests.Infrastructure;
using Xunit;

namespace POS.IntegrationTests;

/// <summary>
/// The separation between the two clients, tested from the outside.
///
/// These do not assert that policies are configured a particular way — they assert what a caller
/// actually gets back, which is the only thing that protects anything. A refactor that quietly
/// removed a scheme pin would leave the configuration looking fine and fail here.
/// </summary>
public class DashboardAuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DashboardAuthenticationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---------------------------------------------------------------- helpers

    private HttpClient Provisioning()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Provisioning-Key", CustomWebApplicationFactory.ProvisioningKey);
        return client;
    }

    private HttpClient WithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<StoreDto> CreateStoreAsync(string? name = null)
    {
        var response = await Provisioning().PostAsJsonAsync("/api/provisioning/stores",
            new CreateStoreRequest(
                "D" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
                name ?? "Dashboard Test Store", null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<StoreDto>>(JsonOpts))!.Data!;
    }

    /// <summary>Pre-authorises an address through the bootstrap path, as an operator would.</summary>
    private async Task<DashboardUserDto> GrantAsync(string email, DashboardRole role, Guid? storeId = null)
    {
        var response = await Provisioning().PostAsJsonAsync("/api/provisioning/dashboard-users",
            new GrantDashboardAccessRequest(email, "Test User", role, storeId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<DashboardUserDto>>(JsonOpts))!.Data!;
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    /// <summary>Grants access and returns a client carrying a token for that person.</summary>
    private async Task<HttpClient> GrantedClientAsync(DashboardRole role, Guid? storeId = null)
    {
        var email = NewEmail();
        await GrantAsync(email, role, storeId);
        return WithToken(_factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), email));
    }

    /// <summary>Enrols a till and returns a client carrying its POS bearer token.</summary>
    private async Task<HttpClient> TerminalClientAsync()
    {
        var store = await CreateStoreAsync();

        var codeResponse = await Provisioning().PostAsJsonAsync(
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

        return WithToken(token.AccessToken);
    }

    // ------------------------------------------------ dashboard needs Neon Auth

    [Fact]
    public async Task Dashboard_WithoutAnyToken_Is401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/dashboard/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_WithAValidNeonTokenAndAGrant_ReturnsTheCallersIdentity()
    {
        var email = NewEmail();
        var subject = Guid.NewGuid().ToString();
        var granted = await GrantAsync(email, DashboardRole.Manager);

        var response = await WithToken(_factory.NeonAuth.IssueToken(subject, email, name: "Ada"))
            .GetAsync("/api/dashboard/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DashboardIdentityResponse>>(JsonOpts);
        body!.Data!.UserId.Should().Be(granted.Id);
        body.Data.Subject.Should().Be(subject);
        body.Data.Email.Should().Be(email);
        body.Data.Role.Should().Be(nameof(DashboardRole.Manager));
        body.Data.IsStoreScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Dashboard_Summary_ReadsRealDataThroughTheApplicationLayer()
    {
        var store = await CreateStoreAsync("Summary Store");
        var client = await GrantedClientAsync(DashboardRole.Viewer);

        var response = await client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await response.Content.ReadFromJsonAsync<ApiResponse<DashboardSummaryResponse>>(JsonOpts))!.Data!;

        summary.Scope.AllStores.Should().BeTrue();
        summary.Totals.Stores.Should().BeGreaterThan(0);
        summary.Stores.Should().Contain(s => s.StoreId == store.Id);
    }

    // -------------------------------------- a valid token is not, by itself, access

    [Fact]
    public async Task Dashboard_WithAValidNeonTokenButNoGrant_Is403NotAuthorised()
    {
        // The token is genuine and correctly signed. Anyone can sign up to Neon Auth, so being
        // able to produce one must not be enough — this is the distinction the whole design rests
        // on, and 403 rather than 401 is how the API says "I know exactly who you are, and no".
        var response = await WithToken(_factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), NewEmail()))
            .GetAsync("/api/dashboard/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_WithAnUnverifiedEmail_CannotClaimAGrant()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        // Registering an unverified account under someone else's address must not inherit their
        // grant. Without this check, granting by email would be an account-takeover primitive.
        var response = await WithToken(
                _factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), email, emailVerified: false))
            .GetAsync("/api/dashboard/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_WhenAGrantIsAlreadyBoundToAnotherAccount_RefusesTheSecond()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Viewer);

        var first = await WithToken(_factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), email))
            .GetAsync("/api/dashboard/me");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // A different Neon account presenting the same verified address. The binding is one-way,
        // so the grant stays with whoever claimed it.
        var second = await WithToken(_factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), email))
            .GetAsync("/api/dashboard/me");

        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_WhenAGrantIsDeactivated_AccessStopsOnTheNextRequest()
    {
        var email = NewEmail();
        var subject = Guid.NewGuid().ToString();
        var granted = await GrantAsync(email, DashboardRole.Viewer);
        var client = WithToken(_factory.NeonAuth.IssueToken(subject, email));

        (await client.GetAsync("/api/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Withdrawal goes through the admin surface: the bootstrap key deliberately only creates
        // and lists, so day-to-day access management needs a real administrator.
        var admin = await GrantedClientAsync(DashboardRole.Admin);
        (await admin.PostAsync($"/api/admin/dashboard-users/{granted.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The same token, unexpired. Access is decided from the database on every request, so it
        // stops working immediately rather than when the token would have expired.
        (await client.GetAsync("/api/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_ForABannedAccount_Is403()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        var response = await WithToken(
                _factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), email, banned: true))
            .GetAsync("/api/dashboard/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----------------------------------------------------- token validation itself

    [Fact]
    public async Task Dashboard_WithATokenSignedByAnotherKey_Is401()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        var forged = _factory.NeonAuth.IssueTokenSignedByAnotherKey(Guid.NewGuid().ToString(), email);

        (await WithToken(forged).GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_WithTheWrongIssuer_Is401()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        var token = _factory.NeonAuth.IssueToken(
            Guid.NewGuid().ToString(), email, issuer: "https://some-other-neon-project.test");

        (await WithToken(token).GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_WithTheWrongAudience_Is401()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        var token = _factory.NeonAuth.IssueToken(
            Guid.NewGuid().ToString(), email, audience: "https://someone-elses-api.test");

        (await WithToken(token).GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_WithAnExpiredToken_Is401()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Admin);

        // Beyond the configured clock skew, so this is expiry and not tolerance.
        var token = _factory.NeonAuth.IssueToken(
            Guid.NewGuid().ToString(), email, lifetime: TimeSpan.FromMinutes(-10));

        (await WithToken(token).GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_WithGarbageInTheAuthorizationHeader_Is401()
    {
        (await WithToken("not-a-token").GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------ claims from the token are not trusted

    [Fact]
    public async Task Dashboard_WhenTheTokenForgesTheApisOwnClaims_TheyAreIgnored()
    {
        // A Neon Auth profile can carry arbitrary fields, so the token could arrive already
        // claiming to be a provisioned administrator. Those claim names are stripped before
        // anything looks at them, and the answer is the same as for any unprovisioned account.
        var token = _factory.NeonAuth.IssueToken(
            Guid.NewGuid().ToString(),
            NewEmail(),
            extraClaims: new Dictionary<string, object?>
            {
                ["token_kind"] = "dashboard",
                ["dashboard_user_id"] = Guid.NewGuid().ToString(),
                ["store_scope"] = Guid.NewGuid().ToString(),
                [ClaimTypes.Role] = nameof(DashboardRole.Admin)
            });

        (await WithToken(token).GetAsync("/api/dashboard/me"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_WhenAViewerForgesTheAdminRole_ItIsStillRefused()
    {
        var email = NewEmail();
        await GrantAsync(email, DashboardRole.Viewer);

        // A real grant, so this caller is genuinely authorised for the dashboard — but the role
        // comes from the database, and the one in the token is discarded.
        var token = _factory.NeonAuth.IssueToken(
            Guid.NewGuid().ToString(),
            email,
            extraClaims: new Dictionary<string, object?>
            {
                [ClaimTypes.Role] = nameof(DashboardRole.Admin),
                ["role"] = nameof(DashboardRole.Admin)
            });

        var client = WithToken(token);

        (await client.GetAsync("/api/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/admin/dashboard-users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ roles

    [Fact]
    public async Task Admin_WithAViewerGrant_Is403()
    {
        var client = await GrantedClientAsync(DashboardRole.Viewer);

        (await client.GetAsync("/api/admin/dashboard-users"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_WithAnAdminGrant_Succeeds()
    {
        var client = await GrantedClientAsync(DashboardRole.Admin);

        var response = await client.GetAsync("/api/admin/dashboard-users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = (await response.Content
            .ReadFromJsonAsync<ApiResponse<IReadOnlyList<DashboardUserDto>>>(JsonOpts))!.Data!;
        users.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Admin_WithoutAnyToken_Is401()
    {
        (await _factory.CreateClient().GetAsync("/api/admin/dashboard-users"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------- tenant isolation

    [Fact]
    public async Task Dashboard_AStoreScopedUser_SeesOnlyTheirOwnStore()
    {
        var mine = await CreateStoreAsync("Mine");
        var theirs = await CreateStoreAsync("Theirs");

        var client = await GrantedClientAsync(DashboardRole.Manager, mine.Id);

        var summary = (await (await client.GetAsync("/api/dashboard/summary")).Content
            .ReadFromJsonAsync<ApiResponse<DashboardSummaryResponse>>(JsonOpts))!.Data!;

        summary.Scope.AllStores.Should().BeFalse();
        summary.Scope.StoreId.Should().Be(mine.Id);
        summary.Totals.Stores.Should().Be(1);
        summary.Stores.Should().ContainSingle().Which.StoreId.Should().Be(mine.Id);
        summary.Stores.Should().NotContain(s => s.StoreId == theirs.Id);
    }

    // ----------------------------------------- the two clients cannot swap places

    [Fact]
    public async Task Dashboard_WithAPosTerminalToken_Is401()
    {
        // The terminal token is valid, unexpired and signed with a key this API holds. It is
        // simply not the key the dashboard scheme verifies against, so it never authenticates
        // here at all — the separation is cryptographic, not a matter of checking a claim.
        var terminal = await TerminalClientAsync();

        (await terminal.GetAsync("/api/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await terminal.GetAsync("/api/dashboard/summary")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await terminal.GetAsync("/api/admin/dashboard-users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_WithANeonDashboardToken_Is401()
    {
        var admin = await GrantedClientAsync(DashboardRole.Admin);

        var push = await admin.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(
        [
            new SyncEventPayload(Guid.NewGuid(), "Order", Guid.NewGuid(), "Upsert",
                """{"invoiceNumber":"INV-1"}""", 1, DateTime.UtcNow)
        ]));

        push.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/api/sync/pull")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/api/sync/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/api/terminals/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provisioning_WithADashboardAdminToken_IsStillRefused()
    {
        // The provisioning key is a separate credential and is not superseded by an admin grant.
        var admin = await GrantedClientAsync(DashboardRole.Admin);

        (await admin.GetAsync("/api/provisioning/stores")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_WithATerminalToken_StillWorksAlongsideDashboardAuthentication()
    {
        // The regression that matters most: a till that has been offline comes back and syncs,
        // using nothing but the credential it already had. Adding a second authentication scheme
        // must be invisible from here.
        var terminal = await TerminalClientAsync();

        var push = await terminal.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(
        [
            new SyncEventPayload(Guid.NewGuid(), "Order", Guid.NewGuid(), "Upsert",
                """{"invoiceNumber":"INV-OFFLINE-1"}""", 1, DateTime.UtcNow)
        ]));

        push.StatusCode.Should().Be(HttpStatusCode.OK);
        (await push.Content.ReadFromJsonAsync<ApiResponse<SyncPushResponse>>(JsonOpts))!
            .Data!.Accepted.Should().Be(1);

        (await terminal.GetAsync("/api/sync/status")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await terminal.GetAsync("/api/sync/pull")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await terminal.GetAsync("/api/terminals/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// A deployment that never configured Neon Auth must refuse dashboard traffic, not serve it.
/// </summary>
public class DashboardUnconfiguredTests : IClassFixture<NeonAuthUnconfiguredWebApplicationFactory>
{
    private readonly NeonAuthUnconfiguredWebApplicationFactory _factory;

    public DashboardUnconfiguredTests(NeonAuthUnconfiguredWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Dashboard_WhenNeonAuthIsNotConfigured_RefusesEveryRequest()
    {
        var token = _factory.NeonAuth.IssueToken(Guid.NewGuid().ToString(), "someone@example.test");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await client.GetAsync("/api/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/admin/dashboard-users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_IsUnaffectedByMissingDashboardConfiguration()
    {
        // The POS path must not acquire a dependency on dashboard configuration. An unauthenticated
        // call still answers 401 from the terminal scheme, exactly as it always did.
        var response = await _factory.CreateClient().GetAsync("/api/sync/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
