// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading.Tasks;

using AwesomeAssertions;

using T5S.BadgeButler.Api.Persistence;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

public class InMemoryBadgeStoreTests
{
    private static readonly BadgeMetrics Metrics = new(10, 20, 1, 2, 15);

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenBadgeDoesNotExist()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();

        Badge? badge = await store.GetAsync("missing", Ct);

        badge.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_CreatesANewBadge()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();

        UpsertResult result = await store.UpsertAsync("k1", "label", "message", "0000FF", Metrics, providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Created);
        badge.Should().NotBeNull();
        badge!.Label.Should().Be("label");
        badge.Color.Should().Be("0000FF");
    }

    [Fact]
    public async Task UpsertAsync_ReplacesAnExistingBadge()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "old", "old", "0000FF", Metrics, providedAccessKey: null, Ct);

        UpsertResult result = await store.UpsertAsync("k1", "new", "new", "008000", Metrics, providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Updated);
        badge!.Label.Should().Be("new");
        badge.Color.Should().Be("008000");
    }

    [Fact]
    public async Task UpsertAsync_OnUnprotectedBadge_SetsTheKeyFromWhateverIsProvided()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: null, Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: "secret", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Updated);
        badge!.AccessKey.Should().Be("secret");
    }

    [Fact]
    public async Task UpsertAsync_OnProtectedBadge_RejectsWrongKey()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: "secret", Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "c", "0000FF", Metrics, providedAccessKey: "wrong", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Unauthorized);
        badge!.Message.Should().Be("b", "an unauthorized upsert must not modify the badge");
    }

    [Fact]
    public async Task UpsertAsync_OnProtectedBadge_AllowsMatchingKey()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: "secret", Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "c", "0000FF", Metrics, providedAccessKey: "secret", Ct);

        result.Should().Be(UpsertResult.Updated);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsNotFound_WhenBadgeDoesNotExist()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();

        DeleteResult result = await store.DeleteAsync("missing", providedAccessKey: null, Ct);

        result.Should().Be(DeleteResult.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_OnProtectedBadge_RejectsMissingKey()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: "secret", Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(DeleteResult.Unauthorized);
        badge.Should().NotBeNull("an unauthorized delete must not remove the badge");
    }

    [Fact]
    public async Task DeleteAsync_OnProtectedBadge_AllowsMatchingKey()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: "secret", Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: "secret", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(DeleteResult.Success);
        badge.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_OnUnprotectedBadge_AllowsAnyone()
    {
        InMemoryBadgeStore store = new InMemoryBadgeStore();
        await store.UpsertAsync("k1", "a", "b", "0000FF", Metrics, providedAccessKey: null, Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: "whatever", Ct);

        result.Should().Be(DeleteResult.Success);
    }
}
