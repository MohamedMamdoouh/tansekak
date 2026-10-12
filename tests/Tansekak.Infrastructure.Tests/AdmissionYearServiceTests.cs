using Microsoft.EntityFrameworkCore;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Domain.Entities;
using Tansekak.Domain.Enums;
using Tansekak.Infrastructure.Persistence;
using Tansekak.Infrastructure.Services;

namespace Tansekak.Infrastructure.Tests;

public class AdmissionYearServiceTests
{
    [Fact]
    public async Task CreateAsync_rejects_when_any_year_exists()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2026, MaximumScore = 320, IsCurrent = true });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var ex = await Assert.ThrowsAsync<ValidationException>(
                () => service.CreateAsync(new CreateAdmissionYearDto(2027, 320)));

            Assert.Equal(ApiErrorCodes.AdmissionYearLimitReached, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task CreateAsync_sets_is_current_true()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            var service = CreateService(db);
            var created = await service.CreateAsync(new CreateAdmissionYearDto(2026, 320));

            Assert.True(created.IsCurrent);
            var stored = await db.AdmissionYears.SingleAsync();
            Assert.True(stored.IsCurrent);
        }
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_year()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2026, MaximumScore = 320, IsCurrent = true });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var ex = await Assert.ThrowsAsync<ValidationException>(
                () => service.CreateAsync(new CreateAdmissionYearDto(2026, 320)));

            Assert.Equal(ApiErrorCodes.AdmissionYearLimitReached, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task UpdateAsync_rejects_duplicate_year()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2026, MaximumScore = 320, IsCurrent = true });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var updated = await service.UpdateAsync(1, new UpdateAdmissionYearDto(2027, 320));

            Assert.NotNull(updated);
            Assert.Equal(2027, updated.Year);
        }
    }

    [Fact]
    public async Task DeleteAsync_cascades_cutoffs_and_results()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.Governorates.Add(new Governorate { Id = 1, NameAr = "القاهرة" });
            db.Universities.Add(new University
            {
                Id = 1,
                NameAr = "جامعة القاهرة",
                GovernorateId = 1,
                Type = UniversityType.Public
            });
            db.Faculties.Add(new Faculty
            {
                Id = 1,
                NameAr = "طب",
                AllowedTracks = [AcademicTrack.Science]
            });
            db.UniversityFaculties.Add(new UniversityFaculty
            {
                Id = 1,
                UniversityId = 1,
                FacultyId = 1
            });
            db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2026, MaximumScore = 320, IsCurrent = true });
            db.AdmissionCutoffs.Add(new AdmissionCutoff
            {
                Id = 1,
                AdmissionYearId = 1,
                UniversityFacultyId = 1,
                Track = AcademicTrack.Science,
                CutoffScore = 350
            });
            db.StudentResults.Add(new StudentResult
            {
                Id = 1,
                AdmissionYearId = 1,
                SeatingNo = "12345",
                ArabicName = "طالب",
                TotalDegree = 380
            });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var deleted = await service.DeleteAsync(1);

            Assert.True(deleted);
            Assert.Empty(await db.AdmissionYears.ToListAsync());
            Assert.Empty(await db.AdmissionCutoffs.ToListAsync());
            Assert.Empty(await db.StudentResults.ToListAsync());
        }
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            var service = CreateService(db);
            var deleted = await service.DeleteAsync(99);

            Assert.False(deleted);
        }
    }

    [Fact]
    public async Task DeleteAsync_promotes_remaining_year_when_current_deleted()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            // Legacy multi-year state from before single-year + delete replaced publish.
            db.AdmissionYears.AddRange(
                new AdmissionYear { Id = 1, Year = 2025, MaximumScore = 320, IsCurrent = true },
                new AdmissionYear { Id = 2, Year = 2026, MaximumScore = 320, IsCurrent = false });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var deleted = await service.DeleteAsync(1);

            Assert.True(deleted);
            var remaining = await db.AdmissionYears.AsNoTracking().SingleAsync();
            Assert.Equal(2, remaining.Id);
            Assert.True(remaining.IsCurrent);
        }
    }

    [Fact]
    public async Task UpdateAsync_sets_is_current_when_none_current()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2026, MaximumScore = 320, IsCurrent = false });
            await db.SaveChangesAsync();

            var service = CreateService(db);
            var updated = await service.UpdateAsync(1, new UpdateAdmissionYearDto(2026, 410));

            Assert.NotNull(updated);
            Assert.True(updated.IsCurrent);
            Assert.True((await db.AdmissionYears.SingleAsync()).IsCurrent);
        }
    }

    private static AdmissionYearService CreateService(AppDbContext db) =>
        new(db, new EntityIdAllocator(db));
}
