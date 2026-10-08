// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.Persistence;
using T5S.BadgeButler.Api.RequestDtos;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

/// <summary>Integration tests against a real Postgres server - see <see cref="PostgresTestDatabase"/>.</summary>
public class PostgresStorageTests
{
    // Mirrors DatabaseStorage.CurrentSchemaVersion (protected there) - bump together with it.
    private const int CurrentSchemaVersion = 2;

    // v0.2.0's table, created before schema_info existed (schema version 1).
    private const string V1BadgesTable = """
                                         CREATE TABLE badges (
                                             key TEXT PRIMARY KEY,
                                             label VARCHAR(255) NOT NULL,
                                             message VARCHAR(255) NOT NULL,
                                             color CHAR(6) NOT NULL CHECK (color ~ '^[0-9a-fA-F]{6}$'),
                                             access_key TEXT NULL,
                                             label_width REAL NOT NULL,
                                             message_width REAL NOT NULL,
                                             label_base_x REAL NOT NULL,
                                             message_base_x REAL NOT NULL,
                                             font_height REAL NOT NULL,
                                             updated_at TIMESTAMPTZ NOT NULL
                                         )
                                         """;

    // Same statement used once by hand to bring the live v0.2.0 database under versioning.
    private const string V1SchemaInfoBootstrap = """
                                                 CREATE TABLE schema_info (
                                                     id INT PRIMARY KEY CHECK (id = 1),
                                                     schema_version INT NOT NULL,
                                                     constants_fingerprint TEXT NOT NULL
                                                 );
                                                 INSERT INTO schema_info (id, schema_version, constants_fingerprint)
                                                 VALUES (1, 1, 'pre-versioning-placeholder');
                                                 """;

    private static readonly BadgeAppearance StoredAppearance = new(10, 20, "44CC11", "000000");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Stand-in for BadgeSvgRenderer.CalculateAppearance whose result shows what it was called with.
    private static BadgeAppearance FakeCalculate(BadgeWrite write) => new(write.Label.Length, write.Message.Length, write.Color, "ABCDEF");

    private static BadgeAppearance MustNotCalculate(BadgeWrite write) => throw new InvalidOperationException("appearance should not be recalculated");

    [Fact]
    public async Task InitializeAsync_OnEmptyDatabase_CreatesCurrentSchemaAndRecordsVersionAndFingerprint()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);

        InitializationResult result = await storage.InitializeAsync(MustNotCalculate, Ct);

        result.Should().Be(InitializationResult.Success);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
        (await database.ScalarAsync<string>("SELECT constants_fingerprint FROM schema_info", Ct)).Should().Be(BadgeSvgRenderer.MeasurementFingerprint);
    }

    [Fact]
    public async Task UpsertAndGet_RoundTripEveryField()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);
        await storage.InitializeAsync(MustNotCalculate, Ct);

        UpsertResult result = await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: "secret", Ct);
        Badge? badge = await storage.GetAsync("k1", Ct);

        result.Should().Be(UpsertResult.Created);
        badge.Should().NotBeNull();
        badge!.Label.Should().Be("build");
        badge.Message.Should().Be("passing");
        badge.AccessKey.Should().Be("secret");
        badge.Appearance.Should().Be(StoredAppearance);
        badge.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_KeepsExistingBadgesWithoutRecalculating()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);
        await storage.InitializeAsync(MustNotCalculate, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: "secret", Ct);

        InitializationResult result = await storage.InitializeAsync(MustNotCalculate, Ct);

        result.Should().Be(InitializationResult.Success);
        Badge? badge = await storage.GetAsync("k1", Ct);
        badge!.AccessKey.Should().Be("secret");
        badge.Appearance.Should().Be(StoredAppearance);
        (await database.ScalarAsync<long>("SELECT count(*) FROM schema_info", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_WhenFingerprintIsStale_RecalculatesEveryBadgeAndKeepsUpdatedAt()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);
        await storage.InitializeAsync(MustNotCalculate, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: null, Ct);
        await storage.UpsertAsync("k2", "tests", "42 passed", StoredAppearance with { MessageBackgroundHex = "E05D44" }, providedAccessKey: null, Ct);
        DateTimeOffset updatedAt = (await storage.GetAsync("k1", Ct))!.UpdatedAt;
        await database.ExecuteAsync("UPDATE schema_info SET constants_fingerprint = 'stale'", Ct);

        await storage.InitializeAsync(FakeCalculate, Ct);

        Badge? first = await storage.GetAsync("k1", Ct);
        first!.Appearance.Should().Be(FakeCalculate(new BadgeWrite("build", "passing", "44CC11")));
        first.UpdatedAt.Should().Be(updatedAt);
        (await storage.GetAsync("k2", Ct))!.Appearance.Should().Be(FakeCalculate(new BadgeWrite("tests", "42 passed", "E05D44")));
        (await database.ScalarAsync<string>("SELECT constants_fingerprint FROM schema_info", Ct)).Should().Be(BadgeSvgRenderer.MeasurementFingerprint);
    }

    [Fact]
    public async Task InitializeAsync_OnV1Database_MigratesToCurrentSchemaKeepingBadges()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV1DatabaseAsync(database);
        DateTimeOffset updatedAt = await database.ScalarAsync<DateTime>("SELECT updated_at FROM badges WHERE key = 'badge-butler-build'", Ct);
        PostgresStorage storage = new(database.ConnectionString);

        InitializationResult result = await storage.InitializeAsync(BadgeSvgRenderer.CalculateAppearance, Ct);

        result.Should().Be(InitializationResult.Success);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
        Badge? badge = await storage.GetAsync("badge-butler-build", Ct);
        badge.Should().NotBeNull();
        badge!.Label.Should().Be("build");
        badge.Message.Should().Be("passing");
        badge.AccessKey.Should().Be("secret");
        badge.UpdatedAt.Should().Be(updatedAt);
        badge.Appearance.Should().Be(BadgeSvgRenderer.CalculateAppearance(new BadgeWrite("build", "passing", "44CC11")));
    }

    [Fact]
    public async Task InitializeAsync_OnV1Database_ProducesTheSameSchemaAsAFreshCreate()
    {
        await using PostgresTestDatabase migrated = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV1DatabaseAsync(migrated);
        await new PostgresStorage(migrated.ConnectionString).InitializeAsync(BadgeSvgRenderer.CalculateAppearance, Ct);
        await using PostgresTestDatabase fresh = await PostgresTestDatabase.CreateAsync(Ct);
        await new PostgresStorage(fresh.ConnectionString).InitializeAsync(MustNotCalculate, Ct);

        (await DescribeBadgesTableAsync(migrated)).Should().Be(await DescribeBadgesTableAsync(fresh));
    }

    [Fact]
    public async Task InitializeAsync_OnNewerSchemaVersion_FailsAndLeavesTheDatabaseUntouched()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);
        await storage.InitializeAsync(MustNotCalculate, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: null, Ct);
        await database.ExecuteAsync("UPDATE schema_info SET schema_version = 99", Ct);

        InitializationResult result = await storage.InitializeAsync(MustNotCalculate, Ct);

        result.Should().Be(InitializationResult.Failed);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(99);
        (await storage.GetAsync("k1", Ct))!.Appearance.Should().Be(StoredAppearance);
    }

    [Fact]
    public async Task InitializeAsync_WithoutSchemaInfo_RecreatesTheTablesDeletingExistingBadges()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(V1BadgesTable, Ct);
        await database.ExecuteAsync("INSERT INTO badges VALUES ('k1', 'build', 'passing', '44CC11', NULL, 0, 0, 0, 0, 0, now())", Ct);
        PostgresStorage storage = new(database.ConnectionString);

        await storage.InitializeAsync(MustNotCalculate, Ct);

        (await storage.GetAsync("k1", Ct)).Should().BeNull();
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentStartups_AllSucceed()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);

        InitializationResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => new PostgresStorage(database.ConnectionString).InitializeAsync(MustNotCalculate, Ct)));

        results.Should().AllBeEquivalentTo(InitializationResult.Success);
        (await database.ScalarAsync<long>("SELECT count(*) FROM schema_info", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task UpsertAndDelete_EnforceTheBadgeAccessKey()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = new(database.ConnectionString);
        await storage.InitializeAsync(MustNotCalculate, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: "secret", Ct);

        (await storage.UpsertAsync("k1", "build", "failing", StoredAppearance, providedAccessKey: "wrong", Ct)).Should().Be(UpsertResult.Unauthorized);
        (await storage.DeleteAsync("k1", providedAccessKey: null, Ct)).Should().Be(DeleteResult.Unauthorized);
        (await storage.UpsertAsync("k1", "build", "failing", StoredAppearance, providedAccessKey: "secret", Ct)).Should().Be(UpsertResult.Updated);
        (await storage.GetAsync("k1", Ct))!.Message.Should().Be("failing");
        (await storage.DeleteAsync("k1", providedAccessKey: "secret", Ct)).Should().Be(DeleteResult.Success);
        (await storage.GetAsync("k1", Ct)).Should().BeNull();
    }

    private static async Task CreateV1DatabaseAsync(PostgresTestDatabase database)
    {
        await database.ExecuteAsync(V1BadgesTable, Ct);
        await database.ExecuteAsync(
            "INSERT INTO badges VALUES ('badge-butler-build', 'build', 'passing', '44CC11', 'secret', 0, 0, 0, 0, 0, now() - interval '1 day')", Ct);
        await database.ExecuteAsync(V1SchemaInfoBootstrap, Ct);
    }

    // Column order is ignored on purpose: ALTER TABLE ADD COLUMN can only append.
    private static Task<string> DescribeBadgesTableAsync(PostgresTestDatabase database) =>
        database.ScalarAsync<string>(
            """
            SELECT (SELECT string_agg(format('%s %s(%s) nullable=%s', column_name, data_type, character_maximum_length, is_nullable), E'\n' ORDER BY column_name)
                    FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'badges')
                || E'\n'
                || (SELECT string_agg(format('%s: %s', conname, pg_get_constraintdef(oid)), E'\n' ORDER BY conname)
                    FROM pg_constraint WHERE conrelid = 'badges'::regclass)
            """,
            Ct);
}
