using Microsoft.EntityFrameworkCore;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Application.Interfaces;
using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Persistence;

namespace Tansekak.Infrastructure.Services;

public class AdmissionYearService(
    AppDbContext db,
    EntityIdAllocator idAllocator) : IAdmissionYearService
{
    public async Task<IReadOnlyList<AdmissionYearDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.AdmissionYears.AsNoTracking().OrderByDescending(x => x.Year)
            .Select(x => new AdmissionYearDto(x.Id, x.Year, x.MaximumScore, x.IsCurrent)).ToListAsync(cancellationToken);

    public async Task<AdmissionYearDto?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        await db.AdmissionYears.AsNoTracking()
            .Where(x => x.IsCurrent)
            .Select(x => new AdmissionYearDto(x.Id, x.Year, x.MaximumScore, x.IsCurrent))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AdmissionYearDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        ServiceGuards.NotFoundIfNull(
            await db.AdmissionYears.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new AdmissionYearDto(x.Id, x.Year, x.MaximumScore, x.IsCurrent))
                .FirstOrDefaultAsync(cancellationToken),
            ApiErrorCodes.AdmissionYearNotFound);

    public async Task<AdmissionYearDto> CreateAsync(CreateAdmissionYearDto dto, CancellationToken cancellationToken = default)
    {
        await EnsureNoYearExistsAsync(cancellationToken);
        await EnsureYearUniqueAsync(dto.Year, null, cancellationToken);

        var entity = new AdmissionYear
        {
            Id = await idAllocator.NextAsync(EntityIdAllocator.AdmissionYears, cancellationToken),
            Year = dto.Year,
            MaximumScore = dto.MaximumScore,
            IsCurrent = true
        };
        db.AdmissionYears.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new AdmissionYearDto(entity.Id, entity.Year, entity.MaximumScore, entity.IsCurrent);
    }

    public async Task<AdmissionYearDto?> UpdateAsync(int id, UpdateAdmissionYearDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await db.AdmissionYears.FindAsync([id], cancellationToken);
        ServiceGuards.NotFoundIfNull(entity, ApiErrorCodes.AdmissionYearNotFound);

        await EnsureYearUniqueAsync(dto.Year, id, cancellationToken);

        var maxStoredScore = await db.StudentResults
            .Where(x => x.AdmissionYearId == id)
            .MaxAsync(x => (decimal?)x.TotalDegree, cancellationToken);

        if (maxStoredScore is not null && dto.MaximumScore < maxStoredScore.Value)
            throw new ValidationException(ApiErrorCodes.MaximumScoreBelowResults);

        entity!.Year = dto.Year;
        entity.MaximumScore = dto.MaximumScore;
        // Recover legacy / stuck rows where Publish was removed and no year is current.
        if (!entity.IsCurrent &&
            !await db.AdmissionYears.AnyAsync(x => x.IsCurrent, cancellationToken))
        {
            entity.IsCurrent = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AdmissionYearDto(entity.Id, entity.Year, entity.MaximumScore, entity.IsCurrent);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await db.AdmissionYears.AnyAsync(x => x.Id == id, cancellationToken))
            return false;

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (db.Database.IsRelational())
        {
            await db.AdmissionCutoffs
                .Where(x => x.AdmissionYearId == id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.StudentResults
                .Where(x => x.AdmissionYearId == id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.StudentResultImportEvents
                .Where(x => x.AdmissionYearId == id)
                .ExecuteDeleteAsync(cancellationToken);
            await db.AdmissionYears
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var cutoffs = await db.AdmissionCutoffs
                .Where(x => x.AdmissionYearId == id)
                .ToListAsync(cancellationToken);
            db.AdmissionCutoffs.RemoveRange(cutoffs);

            var results = await db.StudentResults
                .Where(x => x.AdmissionYearId == id)
                .ToListAsync(cancellationToken);
            db.StudentResults.RemoveRange(results);

            var importEvents = await db.StudentResultImportEvents
                .Where(x => x.AdmissionYearId == id)
                .ToListAsync(cancellationToken);
            db.StudentResultImportEvents.RemoveRange(importEvents);

            var entity = await db.AdmissionYears.FindAsync([id], cancellationToken);
            if (entity is not null)
                db.AdmissionYears.Remove(entity);

            await db.SaveChangesAsync(cancellationToken);
        }

        // Publish was removed: deleting the current year must not leave remaining
        // (legacy multi-year) rows with IsCurrent=false — that breaks prediction/results
        // and Create is blocked while any year still exists.
        await PromoteNewestYearIfNoCurrentAsync(cancellationToken);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private async Task PromoteNewestYearIfNoCurrentAsync(CancellationToken cancellationToken)
    {
        if (await db.AdmissionYears.AnyAsync(x => x.IsCurrent, cancellationToken))
            return;

        var newest = await db.AdmissionYears
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is null)
            return;

        newest.IsCurrent = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureNoYearExistsAsync(CancellationToken cancellationToken)
    {
        if (await db.AdmissionYears.AnyAsync(cancellationToken))
            throw new ValidationException(ApiErrorCodes.AdmissionYearLimitReached);
    }

    private async Task EnsureYearUniqueAsync(int year, int? excludeId, CancellationToken cancellationToken)
    {
        var duplicate = await db.AdmissionYears.AnyAsync(
            x => x.Year == year && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);

        if (duplicate)
            throw new ValidationException(ApiErrorCodes.AdmissionYearDuplicate);
    }
}
