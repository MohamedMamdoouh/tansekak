using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Application.Interfaces;
using Tansekak.Infrastructure.Identity;
using Tansekak.Infrastructure.Import;

namespace Tansekak.Api.Controllers;

[ApiController]
[Route("api/admin/admission-years/{yearId:int}/student-results")]
[Authorize(Roles = Roles.Administrator)]
public class StudentResultsAdminController(IStudentResultImportService importService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<StudentResultsSummaryDto>>> Summary(
        int yearId,
        CancellationToken ct)
    {
        var summary = await importService.GetSummaryAsync(yearId, ct);
        return Ok(ApiResponse<StudentResultsSummaryDto>.Ok(summary));
    }

    [HttpPost("import")]
    [RequestSizeLimit(StudentResultsImportConstants.MaxFileBytes + 1_048_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = StudentResultsImportConstants.MaxFileBytes + 1_048_576)]
    public async Task<ActionResult<ApiResponse<ImportResultDto>>> Import(
        int yearId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<ImportResultDto>.Fail(ApiErrorCodes.FileRequired));

        if (file.Length > StudentResultsImportConstants.MaxFileBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                ApiResponse<ImportResultDto>.Fail(ApiErrorCodes.FileTooLarge));
        }

        var ext = Path.GetExtension(file.FileName);
        if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<ImportResultDto>.Fail(ApiErrorCodes.OnlyXlsxFiles));

        await using var stream = file.OpenReadStream();
        if (!await HasXlsxSignatureAsync(stream, ct))
            return BadRequest(ApiResponse<ImportResultDto>.Fail(ApiErrorCodes.ImportFileInvalid));

        stream.Position = 0;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "admin";
        var result = await importService.ImportAsync(yearId, stream, file.FileName, email, ct);
        return ToActionResult(result);
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<DeleteStudentResultsDto>>> DeleteAll(int yearId, CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "admin";
        var result = await importService.DeleteAllAsync(yearId, email, ct);
        return Ok(ApiResponse<DeleteStudentResultsDto>.Ok(
            result,
            $"تم حذف {result.DeletedCount} نتيجة."));
    }

    private static async Task<bool> HasXlsxSignatureAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[2];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 2), ct);
        return read == 2 && buffer[0] == 0x50 && buffer[1] == 0x4B;
    }

    private ActionResult<ApiResponse<ImportResultDto>> ToActionResult(ImportResultDto result) =>
        result.Success
            ? Ok(ApiResponse<ImportResultDto>.Ok(result, result.Message))
            : BadRequest(ApiResponse<ImportResultDto>.Fail(
                ApiErrorCodes.ValidationFailed,
                result.Message,
                result.Errors?.Select(e => new ApiError
                {
                    Field = e.Column,
                    Message = e.Message,
                    RowNumber = e.RowNumber,
                    ErrorCode = e.ErrorCode
                }).ToList()));
}
