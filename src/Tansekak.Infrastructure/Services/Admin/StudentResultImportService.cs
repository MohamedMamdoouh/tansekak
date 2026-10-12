using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Application.Interfaces;
using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Import;
using Tansekak.Infrastructure.Persistence;

namespace Tansekak.Infrastructure.Services;

/// <summary>
/// Replaces a year's student results from an .xlsx file. Requires PostgreSQL: rows are streamed into a
/// transaction-scoped temp table with binary COPY, then swapped into StudentResults in the same transaction.
/// </summary>
public sealed class StudentResultImportService(
    AppDbContext db,
    EntityIdAllocator idAllocator,
    ILogger<StudentResultImportService> logger) : IStudentResultImportService
{
    private static readonly TimeSpan BulkCommandTimeout = TimeSpan.FromMinutes(10);

    private static readonly string ValidationFailedMessage =
        ArabicErrorCatalog.GetMessage(ApiErrorCodes.ValidationFailed);

    public async Task<StudentResultsSummaryDto> GetSummaryAsync(
        int yearId,
        CancellationToken cancellationToken = default)
    {
        await EnsureYearExistsAsync(yearId, cancellationToken);

        var total = await db.StudentResults.CountAsync(x => x.AdmissionYearId == yearId, cancellationToken);
        var latest = await db.StudentResultImportEvents.AsNoTracking()
            .Where(x => x.AdmissionYearId == yearId)
            .OrderByDescending(x => x.PerformedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new StudentResultImportEventDto(
                x.Action.ToString(),
                x.FileName,
                x.RowCount,
                x.PerformedAt,
                x.PerformedByEmail))
            .FirstOrDefaultAsync(cancellationToken);

        return new StudentResultsSummaryDto(total, latest);
    }

    public async Task<ImportResultDto> ImportAsync(
        int yearId,
        Stream fileStream,
        string fileName,
        string performedByEmail,
        CancellationToken cancellationToken = default)
    {
        var year = await db.AdmissionYears.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == yearId, cancellationToken)
            ?? throw new NotFoundException(ApiErrorCodes.AdmissionYearNotFound);

        var (stream, tempStream) = await EnsureSeekableStreamAsync(fileStream, cancellationToken);
        await using (tempStream)
        {
            return await ImportCoreAsync(year, stream, fileName, performedByEmail, cancellationToken);
        }
    }

    public async Task<DeleteStudentResultsDto> DeleteAllAsync(
        int yearId,
        string performedByEmail,
        CancellationToken cancellationToken = default)
    {
        await EnsureYearExistsAsync(yearId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireImportLockAsync(yearId, performedByEmail, cancellationToken);

        db.Database.SetCommandTimeout(BulkCommandTimeout);
        var deleted = await db.StudentResults
            .Where(x => x.AdmissionYearId == yearId)
            .ExecuteDeleteAsync(cancellationToken);

        await AddEventAsync(yearId, StudentResultImportAction.Deleted, null, deleted, performedByEmail, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Student results deleted for year {YearId}. Rows={RowCount} Admin={Admin}",
            yearId,
            deleted,
            performedByEmail);

        return new DeleteStudentResultsDto(deleted);
    }

    // The transaction is never rolled back explicitly: disposing it without a commit rolls back,
    // which also covers validation failures, cancellation, and errors during the swap.
    private async Task<ImportResultDto> ImportCoreAsync(
        AdmissionYear year,
        Stream stream,
        string fileName,
        string performedByEmail,
        CancellationToken cancellationToken)
    {
        var fileBytes = stream.Length;
        var elapsed = Stopwatch.StartNew();

        logger.LogInformation(
            "Student results import started for year {YearId}. FileBytes={FileBytes} Admin={Admin}",
            year.Id,
            fileBytes,
            performedByEmail);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireImportLockAsync(year.Id, performedByEmail, cancellationToken);

        StudentResultExcelReadOutcome outcome;
        try
        {
            outcome = await StageRowsAsync(stream, year.MaximumScore, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Student results import cancelled for year {YearId} before the swap. DurationMs={DurationMs} Admin={Admin}",
                year.Id,
                elapsed.ElapsedMilliseconds,
                performedByEmail);
            throw;
        }

        var stagingMs = elapsed.ElapsedMilliseconds;

        if (outcome.TotalErrorCount > 0)
        {
            logger.LogWarning(
                "Student results import rejected for year {YearId}. Errors={ErrorCount} ValidRows={ValidRows} FileBytes={FileBytes} DurationMs={DurationMs} Admin={Admin}",
                year.Id,
                outcome.TotalErrorCount,
                outcome.ValidRowCount,
                fileBytes,
                stagingMs,
                performedByEmail);

            return new ImportResultDto(false, BuildValidationMessage(outcome), Errors: outcome.Errors);
        }

        await SwapFromStagingAsync(year.Id, outcome.ValidRowCount, fileName, performedByEmail);
        await transaction.CommitAsync(CancellationToken.None);

        logger.LogInformation(
            "Student results import completed for year {YearId}. Rows={RowCount} FileBytes={FileBytes} StagingMs={StagingMs} SwapMs={SwapMs} DurationMs={DurationMs} Admin={Admin}",
            year.Id,
            outcome.ValidRowCount,
            fileBytes,
            stagingMs,
            elapsed.ElapsedMilliseconds - stagingMs,
            elapsed.ElapsedMilliseconds,
            performedByEmail);

        return new ImportResultDto(
            true,
            $"تم استيراد {outcome.ValidRowCount} نتيجة طالب (استبدال كامل للسنة).",
            outcome.ValidRowCount);
    }

    private async Task<StudentResultExcelReadOutcome> StageRowsAsync(
        Stream stream,
        decimal maximumScore,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TEMP TABLE staging_student_results (
                seating_no varchar(20) NOT NULL,
                arabic_name varchar(300) NOT NULL,
                total_degree numeric(6,2) NOT NULL
            ) ON COMMIT DROP
            """,
            cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        // Disposing the importer without completing it cancels the COPY, which must happen
        // before the transaction is rolled back.
        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY staging_student_results (seating_no, arabic_name, total_degree) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        stream.Position = 0;
        var outcome = StudentResultExcelReader.ReadAndValidate(
            stream,
            maximumScore,
            row =>
            {
                copy.StartRow();
                copy.Write(row.SeatingNo, NpgsqlDbType.Varchar);
                copy.Write(row.ArabicName, NpgsqlDbType.Varchar);
                copy.Write(row.TotalDegree, NpgsqlDbType.Numeric);
            },
            cancellationToken);

        if (outcome.TotalErrorCount == 0)
            await copy.CompleteAsync(cancellationToken);

        return outcome;
    }

    // Runs with CancellationToken.None: once the swap starts it always completes.
    private async Task SwapFromStagingAsync(
        int yearId,
        int rowCount,
        string fileName,
        string performedByEmail)
    {
        var cancellationToken = CancellationToken.None;
        db.Database.SetCommandTimeout(BulkCommandTimeout);

        var lockedMaximumScore = await db.Database
            .SqlQuery<decimal>($"""
                                SELECT "MaximumScore" AS "Value" FROM "AdmissionYears"
                                WHERE "Id" = {yearId} FOR UPDATE
                                """)
            .ToListAsync(cancellationToken);

        if (lockedMaximumScore.Count == 0)
            throw new NotFoundException(ApiErrorCodes.AdmissionYearNotFound);

        var maxStagedScore = await db.Database
            .SqlQueryRaw<decimal?>("""SELECT MAX(total_degree) AS "Value" FROM staging_student_results""")
            .SingleAsync(cancellationToken);

        if (maxStagedScore > lockedMaximumScore[0])
        {
            throw new ValidationException(
                ApiErrorCodes.ScoreExceedsMax,
                ArabicErrorCatalog.GetMessage(ApiErrorCodes.ScoreExceedsMax, lockedMaximumScore[0]));
        }

        var (startId, _) = await idAllocator.AllocateRangeAsync(
            EntityIdAllocator.StudentResults,
            rowCount,
            cancellationToken);

        await db.StudentResults
            .Where(x => x.AdmissionYearId == yearId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "StudentResults" ("Id", "AdmissionYearId", "SeatingNo", "ArabicName", "TotalDegree")
             SELECT {startId} + ROW_NUMBER() OVER (ORDER BY seating_no) - 1,
                    {yearId},
                    seating_no,
                    arabic_name,
                    total_degree
             FROM staging_student_results
             """,
            cancellationToken);

        await AddEventAsync(yearId, StudentResultImportAction.Imported, fileName, rowCount, performedByEmail, cancellationToken);
    }

    private async Task AddEventAsync(
        int yearId,
        StudentResultImportAction action,
        string? fileName,
        int rowCount,
        string performedByEmail,
        CancellationToken cancellationToken)
    {
        db.StudentResultImportEvents.Add(new StudentResultImportEvent
        {
            Id = await idAllocator.NextAsync(EntityIdAllocator.StudentResultImportEvents, cancellationToken),
            AdmissionYearId = yearId,
            Action = action,
            FileName = fileName,
            RowCount = rowCount,
            PerformedAt = DateTime.UtcNow,
            PerformedByEmail = performedByEmail
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AcquireImportLockAsync(int yearId, string performedByEmail, CancellationToken cancellationToken)
    {
        var acquired = await db.Database
            .SqlQuery<bool>($"""
                             SELECT pg_try_advisory_xact_lock({StudentResultsImportConstants.AdvisoryLockKey}) AS "Value"
                             """)
            .SingleAsync(cancellationToken);

        if (acquired)
            return;

        logger.LogWarning(
            "Student results operation rejected for year {YearId}: another import or delete is running. Admin={Admin}",
            yearId,
            performedByEmail);
        throw new ConflictException(ApiErrorCodes.ImportInProgress);
    }

    private async Task EnsureYearExistsAsync(int yearId, CancellationToken cancellationToken)
    {
        if (!await db.AdmissionYears.AnyAsync(x => x.Id == yearId, cancellationToken))
            throw new NotFoundException(ApiErrorCodes.AdmissionYearNotFound);
    }

    private static string BuildValidationMessage(StudentResultExcelReadOutcome outcome) =>
        $"{ValidationFailedMessage} ({outcome.TotalErrorCount} خطأ)";

    private static async Task<(Stream Stream, FileStream? TempStream)> EnsureSeekableStreamAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        if (fileStream.CanSeek)
            return (fileStream, null);

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"tansekak-import-{Guid.NewGuid():N}.xlsx");

        await using (var tempWrite = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous))
        {
            await fileStream.CopyToAsync(tempWrite, cancellationToken);
        }

        var tempStream = new FileStream(
            tempPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.DeleteOnClose);
        return (tempStream, tempStream);
    }
}
