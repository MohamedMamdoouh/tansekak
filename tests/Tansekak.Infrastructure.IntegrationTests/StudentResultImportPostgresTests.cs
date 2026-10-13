using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Import;
using Tansekak.Infrastructure.Persistence;
using Tansekak.Infrastructure.Services;
using Tansekak.Tests.Shared;

namespace Tansekak.Infrastructure.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class StudentResultImportPostgresTests(PostgresFixture fixture)
{
    private const int YearId = 1;
    private const string Admin = "admin@test.local";

    private static readonly object?[] Header = ["seating_no", "arabic_name", "total_degree"];

    [Fact]
    public async Task Import_replaces_year_results_records_event_and_opens_lookup()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["999"]);

        using var file = TestWorkbook.Create(
            Header,
            [1001660L, "محمد ابو الحسن", 163.5m],
            ["1001661", new InlineText("محمد احمد"), "187.5"],
            [1001662L, "هشام محمد", 212]);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var result = await CreateService(db).ImportAsync(YearId, file, "results.xlsx", Admin);

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, result.ImportedCount);
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var rows = await db.StudentResults.AsNoTracking().OrderBy(x => x.SeatingNo).ToListAsync();
            Assert.Equal(["1001660", "1001661", "1001662"], rows.Select(x => x.SeatingNo));
            Assert.Equal([163.5m, 187.5m, 212m], rows.Select(x => x.TotalDegree));
            Assert.Equal("محمد احمد", rows[1].ArabicName);
            Assert.All(rows, r => Assert.True(r.Id > 1));

            var importEvent = await db.StudentResultImportEvents.AsNoTracking().SingleAsync();
            Assert.Equal(StudentResultImportAction.Imported, importEvent.Action);
            Assert.Equal("results.xlsx", importEvent.FileName);
            Assert.Equal(3, importEvent.RowCount);
            Assert.Equal(Admin, importEvent.PerformedByEmail);

            var summary = await CreateService(db).GetSummaryAsync(YearId);
            Assert.Equal(3, summary.TotalCount);
            Assert.Equal("Imported", summary.LatestEvent!.Action);

            var lookup = await new StudentResultService(db, new CurrentAdmissionYearProvider(db))
                .GetBySeatingNoAsync("1001661");
            Assert.Equal(187.5m, lookup!.TotalDegree);

            Assert.True((await CreateConfigService(db).GetConfigAsync()).StudentResultsAvailable);
        }
    }

    [Fact]
    public async Task Second_import_replaces_first_with_fresh_ids()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            using var first = TestWorkbook.Create(Header, [1L, "أ", 100], [2L, "ب", 200]);
            Assert.True((await CreateService(db).ImportAsync(YearId, first, "first.xlsx", Admin)).Success);
        }

        int[] firstIds;
        await using (var db = PostgresFixture.CreateContext(connectionString))
            firstIds = await db.StudentResults.Select(x => x.Id).ToArrayAsync();

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            using var second = TestWorkbook.Create(Header, [2L, "ب", 250], [3L, "ج", 300]);
            Assert.True((await CreateService(db).ImportAsync(YearId, second, "second.xlsx", Admin)).Success);
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var rows = await db.StudentResults.AsNoTracking().OrderBy(x => x.SeatingNo).ToListAsync();
            Assert.Equal(["2", "3"], rows.Select(x => x.SeatingNo));
            Assert.Equal(250m, rows[0].TotalDegree);
            Assert.Empty(rows.Select(x => x.Id).Intersect(firstIds));

            var summary = await CreateService(db).GetSummaryAsync(YearId);
            Assert.Equal("second.xlsx", summary.LatestEvent!.FileName);
            Assert.Equal(2, await db.StudentResultImportEvents.CountAsync());
        }
    }

    [Fact]
    public async Task Import_with_invalid_rows_keeps_existing_results()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["999"]);

        using var file = TestWorkbook.Create(
            Header,
            [1001660L, "محمد", 200],
            [1001661L, "احمد", 400],
            [1001660L, "مكرر", 150]);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var result = await CreateService(db).ImportAsync(YearId, file, "bad.xlsx", Admin);

            Assert.False(result.Success);
            Assert.DoesNotContain(result.Errors!, e => e.ErrorCode == ApiErrorCodes.ScoreExceedsMax);
            Assert.Contains(result.Errors!, e => e.RowNumber == 4 && e.ErrorCode == ApiErrorCodes.Duplicate);
        }

        await AssertUntouchedAsync(connectionString, "999");
    }

    [Fact]
    public async Task Import_skips_rows_above_the_maximum_and_replaces_with_the_rest()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["999"]);

        using var file = TestWorkbook.Create(
            Header,
            [1001660L, "محمد", 200],
            [1001661L, "احمد", 400],
            [1001662L, "هشام", 320]);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var result = await CreateService(db).ImportAsync(YearId, file, "results.xlsx", Admin);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.ImportedCount);
            Assert.Contains("تم تجاهل 1", result.Message);
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var rows = await db.StudentResults.AsNoTracking().OrderBy(x => x.SeatingNo).ToListAsync();
            Assert.Equal(["1001660", "1001662"], rows.Select(x => x.SeatingNo));
            Assert.Equal([200m, 320m], rows.Select(x => x.TotalDegree));
        }
    }

    [Fact]
    public async Task Import_cancelled_while_reading_keeps_existing_results_and_releases_lock()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["999"]);

        var rows = new List<object?[]> { Header };
        for (var i = 0; i < 20_000; i++)
            rows.Add([1_000_000L + i, $"طالب {i}", 150 + i % 100]);
        using var workbook = TestWorkbook.Create(rows);

        using var cts = new CancellationTokenSource();
        await using var file = new CancelAfterBytesStream(workbook, cts, workbook.Length / 2);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateService(db).ImportAsync(YearId, file, "big.xlsx", Admin, cts.Token));
        }

        await AssertUntouchedAsync(connectionString, "999");

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            using var retry = TestWorkbook.Create(Header, [1001660L, "محمد", 200]);
            Assert.True((await CreateService(db).ImportAsync(YearId, retry, "retry.xlsx", Admin)).Success);
        }
    }

    [Fact]
    public async Task Import_and_delete_return_conflict_while_lock_is_held()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["999"]);

        await using (var holder = new NpgsqlConnection(connectionString))
        {
            await holder.OpenAsync();
            await using var holderTransaction = await holder.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                             $"SELECT pg_advisory_xact_lock({StudentResultsImportConstants.AdvisoryLockKey})",
                             holder,
                             holderTransaction))
            {
                await lockCommand.ExecuteNonQueryAsync();
            }

            await using var db = PostgresFixture.CreateContext(connectionString);
            using var file = TestWorkbook.Create(Header, [1001660L, "محمد", 200]);

            var importConflict = await Assert.ThrowsAsync<ConflictException>(
                () => CreateService(db).ImportAsync(YearId, file, "results.xlsx", Admin));
            Assert.Equal(ApiErrorCodes.ImportInProgress, importConflict.ErrorCode);

            var deleteConflict = await Assert.ThrowsAsync<ConflictException>(
                () => CreateService(db).DeleteAllAsync(YearId, Admin));
            Assert.Equal(ApiErrorCodes.ImportInProgress, deleteConflict.ErrorCode);
        }

        await AssertUntouchedAsync(connectionString, "999");
    }

    [Fact]
    public async Task DeleteAll_removes_year_results_records_event_and_closes_lookup()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["1", "2"]);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var result = await CreateService(db).DeleteAllAsync(YearId, Admin);
            Assert.Equal(2, result.DeletedCount);
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(0, await db.StudentResults.CountAsync());

            var summary = await CreateService(db).GetSummaryAsync(YearId);
            Assert.Equal(0, summary.TotalCount);
            Assert.Equal("Deleted", summary.LatestEvent!.Action);
            Assert.Null(summary.LatestEvent.FileName);
            Assert.Equal(2, summary.LatestEvent.RowCount);

            Assert.False((await CreateConfigService(db).GetConfigAsync()).StudentResultsAvailable);
        }
    }

    [Fact]
    public async Task Import_for_unknown_year_returns_not_found()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString);
        using var file = TestWorkbook.Create(Header, [1001660L, "محمد", 200]);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService(db).ImportAsync(42, file, "results.xlsx", Admin));
        Assert.Equal(ApiErrorCodes.AdmissionYearNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task Year_update_rejects_maximum_below_highest_stored_score()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString, existingSeatingNos: ["1"], existingScore: 300);

        await using var db = PostgresFixture.CreateContext(connectionString);
        var service = new AdmissionYearService(db, new EntityIdAllocator(db));

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(YearId, new UpdateAdmissionYearDto(2027, 299)));
        Assert.Equal(ApiErrorCodes.MaximumScoreBelowResults, ex.ErrorCode);

        var updated = await service.UpdateAsync(YearId, new UpdateAdmissionYearDto(2027, 300));
        Assert.Equal(300, updated!.MaximumScore);
    }

    [Fact]
    public async Task Year_delete_removes_results_and_import_history()
    {
        var connectionString = await fixture.CreateDatabaseAsync();
        await SeedYearAsync(connectionString);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            using var file = TestWorkbook.Create(Header, [1001660L, "محمد", 200]);
            Assert.True((await CreateService(db).ImportAsync(YearId, file, "results.xlsx", Admin)).Success);
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.True(await new AdmissionYearService(db, new EntityIdAllocator(db)).DeleteAsync(YearId));
        }

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(0, await db.AdmissionYears.CountAsync());
            Assert.Equal(0, await db.StudentResults.CountAsync());
            Assert.Equal(0, await db.StudentResultImportEvents.CountAsync());
        }
    }

    private static StudentResultImportService CreateService(AppDbContext db) =>
        new(db, new EntityIdAllocator(db), NullLogger<StudentResultImportService>.Instance);

    private static ConfigService CreateConfigService(AppDbContext db) =>
        new(db, new CurrentAdmissionYearProvider(db), Options.Create(new TansekakOptions()));

    private static async Task SeedYearAsync(
        string connectionString,
        string[]? existingSeatingNos = null,
        decimal existingScore = 100)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        db.AdmissionYears.Add(new AdmissionYear { Id = YearId, Year = 2027, MaximumScore = 320, IsCurrent = true });

        var id = 1;
        foreach (var seatingNo in existingSeatingNos ?? [])
        {
            db.StudentResults.Add(new StudentResult
            {
                Id = id++,
                AdmissionYearId = YearId,
                SeatingNo = seatingNo,
                ArabicName = "قديم",
                TotalDegree = existingScore
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task AssertUntouchedAsync(string connectionString, string existingSeatingNo)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var rows = await db.StudentResults.AsNoTracking().ToListAsync();
        Assert.Equal(existingSeatingNo, Assert.Single(rows).SeatingNo);
        Assert.Equal(0, await db.StudentResultImportEvents.CountAsync());
    }

    /// <summary>Cancels the token once the reader has consumed the given number of bytes.</summary>
    private sealed class CancelAfterBytesStream(Stream inner, CancellationTokenSource cts, long threshold) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _bytesRead += read;
            if (_bytesRead >= threshold)
                cts.Cancel();
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
