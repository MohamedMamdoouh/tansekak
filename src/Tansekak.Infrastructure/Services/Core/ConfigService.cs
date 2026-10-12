using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Application.Interfaces;
using Tansekak.Infrastructure.Persistence;

namespace Tansekak.Infrastructure.Services;

public class ConfigService(
    AppDbContext db,
    CurrentAdmissionYearProvider yearProvider,
    IOptions<TansekakOptions> options) : IConfigService
{
    public async Task<ConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = await yearProvider.GetCurrentAsync(cancellationToken);

        var studentResultsAvailable = await db.StudentResults.AsNoTracking()
            .AnyAsync(x => x.AdmissionYearId == currentYear.Id, cancellationToken);

        return new ConfigDto(
            options.Value.AppName,
            currentYear.Year,
            currentYear.MaximumScore,
            TrackHelper.AllTracks,
            studentResultsAvailable);
    }
}
