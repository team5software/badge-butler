// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.Persistence;
using T5S.BadgeButler.Api.Persistence.Migration.Postgres;
using T5S.BadgeButler.Api.RequestDtos;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

/// <summary>Integration tests against a real Postgres server - see <see cref="PostgresTestDatabase"/>.</summary>
public class PostgresStorageTests
{
    // Mirrors DatabaseStorage.CurrentSchemaVersion (protected there) - bump together with it.
    private const int CurrentSchemaVersion = 3;

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

    // Must never be asked to calculate - for paths where no recalculation is expected.
    private static readonly AppearanceCalculator NoRecalculation = new("fingerprint-a", MustNotCalculate);

    // Stand-in for the renderer whose result shows what it was called with.
    private static readonly AppearanceCalculator FakeCalculator = new("fingerprint-b", FakeCalculate);

    private static readonly AppearanceCalculator RealCalculator = new(BadgeSvgRenderer.AppearanceFingerprint, BadgeSvgRenderer.CalculateAppearance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BadgeAppearance FakeCalculate(BadgeWrite write) => new(write.Label.Length, write.Message.Length, write.Color, "ABCDEF");

    private static BadgeAppearance MustNotCalculate(BadgeWrite write) => throw new InvalidOperationException("appearance should not be recalculated");

    private static PostgresStorage CreateStorage(PostgresTestDatabase database, ILogger<PostgresStorage>? logger = null) =>
        new(database.ConnectionString, logger ?? NullLogger<PostgresStorage>.Instance);

    [Fact]
    public async Task InitializeAsync_OnEmptyDatabase_CreatesCurrentSchemaAndRecordsVersionAndFingerprint()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        ListLogger logger = new();

        InitializationResult result = await CreateStorage(database, logger).InitializeAsync(NoRecalculation, Ct);

        result.Should().Be(InitializationResult.Success);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
        (await database.ScalarAsync<string>("SELECT appearance_fingerprint FROM schema_info", Ct)).Should().Be("fingerprint-a");
        logger.Messages.Should().Equal($"Created database schema version {CurrentSchemaVersion}");
    }

    [Fact]
    public async Task UpsertAndGet_RoundTripEveryField()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = CreateStorage(database);
        await storage.InitializeAsync(NoRecalculation, Ct);

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
        PostgresStorage storage = CreateStorage(database);
        await storage.InitializeAsync(NoRecalculation, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: "secret", Ct);
        ListLogger logger = new();

        InitializationResult result = await CreateStorage(database, logger).InitializeAsync(NoRecalculation, Ct);

        result.Should().Be(InitializationResult.Success);
        Badge? badge = await storage.GetAsync("k1", Ct);
        badge!.AccessKey.Should().Be("secret");
        badge.Appearance.Should().Be(StoredAppearance);
        (await database.ScalarAsync<long>("SELECT count(*) FROM schema_info", Ct)).Should().Be(1);
        logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_WhenFingerprintChanges_RecalculatesEveryBadgeAndKeepsUpdatedAt()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = CreateStorage(database);
        await storage.InitializeAsync(NoRecalculation, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: null, Ct);
        await storage.UpsertAsync("k2", "tests", "42 passed", StoredAppearance with { MessageBackgroundHex = "E05D44" }, providedAccessKey: null, Ct);
        DateTimeOffset updatedAt = (await storage.GetAsync("k1", Ct))!.UpdatedAt;
        ListLogger logger = new();

        await CreateStorage(database, logger).InitializeAsync(FakeCalculator, Ct);

        Badge? first = await storage.GetAsync("k1", Ct);
        first!.Appearance.Should().Be(FakeCalculate(new BadgeWrite("build", "passing", "44CC11")));
        first.UpdatedAt.Should().Be(updatedAt);
        (await storage.GetAsync("k2", Ct))!.Appearance.Should().Be(FakeCalculate(new BadgeWrite("tests", "42 passed", "E05D44")));
        (await database.ScalarAsync<string>("SELECT appearance_fingerprint FROM schema_info", Ct)).Should().Be("fingerprint-b");
        logger.Messages.Should().Equal("Recalculated the appearance of 2 badges (appearance fingerprint changed)");
    }

    [Fact]
    public async Task InitializeAsync_OnV1Database_MigratesToCurrentSchemaKeepingBadges()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV1DatabaseAsync(database);
        DateTimeOffset updatedAt = await database.ScalarAsync<DateTime>("SELECT updated_at FROM badges WHERE key = 'badge-butler-build'", Ct);
        PostgresStorage storage = CreateStorage(database);

        InitializationResult result = await storage.InitializeAsync(RealCalculator, Ct);

        result.Should().Be(InitializationResult.Success);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
        (await database.ScalarAsync<string>("SELECT appearance_fingerprint FROM schema_info", Ct)).Should().Be(BadgeSvgRenderer.AppearanceFingerprint);
        Badge? badge = await storage.GetAsync("badge-butler-build", Ct);
        badge.Should().NotBeNull();
        badge!.Label.Should().Be("build");
        badge.Message.Should().Be("passing");
        badge.AccessKey.Should().Be("secret");
        badge.UpdatedAt.Should().Be(updatedAt);
        badge.Appearance.Should().Be(BadgeSvgRenderer.CalculateAppearance(new BadgeWrite("build", "passing", "44CC11")));
    }

    [Fact]
    public async Task InitializeAsync_OnV2Database_RenamesTheFingerprintColumn()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV2DatabaseAsync(database, storedFingerprint: "fingerprint-a");
        ListLogger logger = new();

        InitializationResult result = await CreateStorage(database, logger).InitializeAsync(FakeCalculator, Ct);

        result.Should().Be(InitializationResult.Success);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
        (await database.ScalarAsync<string>("SELECT appearance_fingerprint FROM schema_info", Ct)).Should().Be("fingerprint-b");
        logger.Messages.Should().Equal(
            "Upgraded database schema from version 2 to 3",
            "Recalculated the appearance of 1 badge (schema upgraded)");
    }

    [Fact]
    public async Task InitializeAsync_AfterAMigration_RecalculatesEvenWhenTheFingerprintMatches()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV2DatabaseAsync(database, storedFingerprint: FakeCalculator.Fingerprint);
        PostgresStorage storage = CreateStorage(database);

        await storage.InitializeAsync(FakeCalculator, Ct);

        (await storage.GetAsync("k1", Ct))!.Appearance.Should().Be(FakeCalculate(new BadgeWrite("build", "passing", "44CC11")));
    }

    [Fact]
    public async Task InitializeAsync_OnV1Database_ProducesTheSameSchemaAsAFreshCreate()
    {
        await using PostgresTestDatabase migrated = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateV1DatabaseAsync(migrated);
        await CreateStorage(migrated).InitializeAsync(RealCalculator, Ct);
        await using PostgresTestDatabase fresh = await PostgresTestDatabase.CreateAsync(Ct);
        await CreateStorage(fresh).InitializeAsync(NoRecalculation, Ct);

        (await DescribeTablesAsync(migrated)).Should().Be(await DescribeTablesAsync(fresh));
    }

    [Fact]
    public async Task InitializeAsync_OnNewerSchemaVersion_FailsAndLeavesTheDatabaseUntouched()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = CreateStorage(database);
        await storage.InitializeAsync(NoRecalculation, Ct);
        await storage.UpsertAsync("k1", "build", "passing", StoredAppearance, providedAccessKey: null, Ct);
        await database.ExecuteAsync("UPDATE schema_info SET schema_version = 99", Ct);
        ListLogger logger = new();

        InitializationResult result = await CreateStorage(database, logger).InitializeAsync(NoRecalculation, Ct);

        result.Should().Be(InitializationResult.Failed);
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(99);
        (await storage.GetAsync("k1", Ct))!.Appearance.Should().Be(StoredAppearance);
        logger.Errors.Should().Equal($"Database schema version 99 is newer than this build supports ({CurrentSchemaVersion})");
    }

    [Fact]
    public async Task InitializeAsync_WithoutSchemaInfo_RecreatesTheTablesDeletingExistingBadges()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(V1BadgesTable, Ct);
        await database.ExecuteAsync("INSERT INTO badges VALUES ('k1', 'build', 'passing', '44CC11', NULL, 0, 0, 0, 0, 0, now())", Ct);
        PostgresStorage storage = CreateStorage(database);

        await storage.InitializeAsync(NoRecalculation, Ct);

        (await storage.GetAsync("k1", Ct)).Should().BeNull();
        (await database.ScalarAsync<int>("SELECT schema_version FROM schema_info", Ct)).Should().Be(CurrentSchemaVersion);
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentStartups_AllSucceed()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);

        InitializationResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => CreateStorage(database).InitializeAsync(NoRecalculation, Ct)));

        results.Should().AllBeEquivalentTo(InitializationResult.Success);
        (await database.ScalarAsync<long>("SELECT count(*) FROM schema_info", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task UpsertAndDelete_EnforceTheBadgeAccessKey()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        PostgresStorage storage = CreateStorage(database);
        await storage.InitializeAsync(NoRecalculation, Ct);
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

    // A v0.3.0 database, built with that release's own migration so it can't drift from the real thing.
    private static async Task CreateV2DatabaseAsync(PostgresTestDatabase database, string storedFingerprint)
    {
        await using (NpgsqlConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await new RemovePositionAndHeight_AddForegroundColor().Create(connection, Ct);
        }

        await database.ExecuteAsync("INSERT INTO schema_info (id, schema_version, constants_fingerprint) VALUES (1, 2, $1)", Ct, storedFingerprint);
        await database.ExecuteAsync(
            "INSERT INTO badges VALUES ('k1', 'build', 'passing', '44CC11', '000000', NULL, 0, 0, now())", Ct);
    }

    // Column order is ignored on purpose: ALTER TABLE ADD COLUMN can only append.
    private static Task<string> DescribeTablesAsync(PostgresTestDatabase database) =>
        database.ScalarAsync<string>(
            """
            SELECT (SELECT string_agg(format('%s.%s %s(%s) nullable=%s', table_name, column_name, data_type, character_maximum_length, is_nullable), E'\n' ORDER BY table_name, column_name)
                    FROM information_schema.columns WHERE table_schema = 'public')
                || E'\n'
                || (SELECT string_agg(format('%s: %s', conname, pg_get_constraintdef(oid)), E'\n' ORDER BY conname)
                    FROM pg_constraint WHERE conrelid IN ('badges'::regclass, 'schema_info'::regclass))
            """,
            Ct);

    /// <summary>Collects formatted log messages so tests can assert on what initialization reports.</summary>
    private sealed class ListLogger : ILogger<PostgresStorage>
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> entries = new();

        public string[] Messages => entries.Where(e => e.Level < LogLevel.Error).Select(e => e.Message).ToArray();

        public string[] Errors => entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Message).ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
