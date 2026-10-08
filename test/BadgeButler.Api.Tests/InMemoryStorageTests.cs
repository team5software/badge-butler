// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading.Tasks;

using AwesomeAssertions;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.Persistence;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

public class InMemoryStorageTests
{
    private static BadgeAppearance Appearance(string background) => new(10, 20, background, "FFFFFF");

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenBadgeDoesNotExist()
    {
        InMemoryStorage store = new();

        Badge? badge = await store.GetAsync("missing", Ct);

        badge.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_CreatesANewBadge()
    {
        InMemoryStorage store = new();

        UpsertResult result = await store.UpsertAsync("k1", "label", "message", Appearance("0000FF"), providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Created);
        badge.Should().NotBeNull();
        badge!.Label.Should().Be("label");
        badge.Appearance.Should().Be(Appearance("0000FF"));
    }

    [Fact]
    public async Task UpsertAsync_ReplacesAnExistingBadge()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "old", "old", Appearance("0000FF"), providedAccessKey: null, Ct);

        UpsertResult result = await store.UpsertAsync("k1", "new", "new", Appearance("008000"), providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Updated);
        badge!.Label.Should().Be("new");
        badge.Appearance.Should().Be(Appearance("008000"));
    }

    [Fact]
    public async Task UpsertAsync_OnUnprotectedBadge_SetsTheKeyFromWhateverIsProvided()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: null, Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: "secret", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Updated);
        badge!.AccessKey.Should().Be("secret");
    }

    [Fact]
    public async Task UpsertAsync_OnProtectedBadge_RejectsWrongKey()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: "secret", Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "c", Appearance("0000FF"), providedAccessKey: "wrong", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Unauthorized);
        badge!.Message.Should().Be("b", "an unauthorized upsert must not modify the badge");
    }

    [Fact]
    public async Task UpsertAsync_OnProtectedBadge_AllowsMatchingKey()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: "secret", Ct);

        UpsertResult result = await store.UpsertAsync("k1", "a", "c", Appearance("0000FF"), providedAccessKey: "secret", Ct);

        result.Should().Be(UpsertResult.Updated);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsNotFound_WhenBadgeDoesNotExist()
    {
        InMemoryStorage store = new();

        DeleteResult result = await store.DeleteAsync("missing", providedAccessKey: null, Ct);

        result.Should().Be(DeleteResult.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_OnProtectedBadge_RejectsMissingKey()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: "secret", Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: null, Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(DeleteResult.Unauthorized);
        badge.Should().NotBeNull("an unauthorized delete must not remove the badge");
    }

    [Fact]
    public async Task DeleteAsync_OnProtectedBadge_AllowsMatchingKey()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: "secret", Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: "secret", Ct);
        Badge? badge = await store.GetAsync("k1", Ct);

        result.Should().Be(DeleteResult.Success);
        badge.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_OnUnprotectedBadge_AllowsAnyone()
    {
        InMemoryStorage store = new();
        await store.UpsertAsync("k1", "a", "b", Appearance("0000FF"), providedAccessKey: null, Ct);

        DeleteResult result = await store.DeleteAsync("k1", providedAccessKey: "whatever", Ct);

        result.Should().Be(DeleteResult.Success);
    }
}
