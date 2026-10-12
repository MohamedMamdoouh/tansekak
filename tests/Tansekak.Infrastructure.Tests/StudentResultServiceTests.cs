using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Services;

namespace Tansekak.Infrastructure.Tests;

public class StudentResultServiceTests
{
    [Fact]
    public async Task GetBySeatingNo_returns_result_for_current_year()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear
            {
                Id = 1,
                Year = 2027,
                MaximumScore = 320,
                IsCurrent = true
            });
            db.StudentResults.Add(new StudentResult
            {
                Id = 1,
                AdmissionYearId = 1,
                SeatingNo = "1001660",
                ArabicName = "محمد",
                TotalDegree = 163.5m
            });
            await db.SaveChangesAsync();

            var service = new StudentResultService(db, new CurrentAdmissionYearProvider(db));
            var result = await service.GetBySeatingNoAsync("1001660");

            Assert.NotNull(result);
            Assert.Equal("1001660", result.SeatingNo);
            Assert.Equal("محمد", result.ArabicName);
            Assert.Equal(163.5m, result.TotalDegree);
            Assert.Equal(2027, result.Year);
            Assert.Equal(320, result.MaximumScore);
        }
    }

    [Fact]
    public async Task GetBySeatingNo_returns_null_when_missing()
    {
        var (db, connection) = TestDbFactory.Create();
        await using (connection)
        await using (db)
        {
            db.AdmissionYears.Add(new AdmissionYear
            {
                Id = 1,
                Year = 2027,
                MaximumScore = 320,
                IsCurrent = true
            });
            await db.SaveChangesAsync();

            var service = new StudentResultService(db, new CurrentAdmissionYearProvider(db));
            var result = await service.GetBySeatingNoAsync("9999999");
            Assert.Null(result);
        }
    }
}
