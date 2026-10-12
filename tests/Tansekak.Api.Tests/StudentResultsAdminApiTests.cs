using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;
using Tansekak.Infrastructure.Import;

namespace Tansekak.Api.Tests;

public class StudentResultsAdminApiTests : IClassFixture<TansekakWebApplicationFactory>
{
    private readonly TansekakWebApplicationFactory _factory;

    public StudentResultsAdminApiTests(TansekakWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Import_rejects_a_non_xlsx_extension()
    {
        var client = await CreateAdminClientAsync();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]), "file", "notes.txt");

        var response = await client.PostAsync("/api/admin/admission-years/1/student-results/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.OnlyXlsxFiles, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Import_rejects_an_xlsx_without_the_zip_signature()
    {
        var client = await CreateAdminClientAsync();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("not a workbook"u8.ToArray()), "file", "results.xlsx");

        var response = await client.PostAsync("/api/admin/admission-years/1/student-results/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ImportFileInvalid, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Import_rejects_a_file_over_50_megabytes()
    {
        var client = await CreateAdminClientAsync();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[StudentResultsImportConstants.MaxFileBytes + 1]), "file", "results.xlsx");

        var response = await client.PostAsync("/api/admin/admission-years/1/student-results/import", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ApiErrorCodes.FileTooLarge, await ErrorCodeAsync(response));
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new LoginRequestDto(TansekakWebApplicationFactory.AdminEmail, TansekakWebApplicationFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errorCode").GetString();
    }
}
