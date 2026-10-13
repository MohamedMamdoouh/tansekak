using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;

namespace Tansekak.Api.Tests;

public class ApiIntegrationTests : IClassFixture<TansekakWebApplicationFactory>
{
    private readonly TansekakWebApplicationFactory _factory;

    public ApiIntegrationTests(TansekakWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Config_returns_current_year_and_tracks()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/config");

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ConfigDto>>();

        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal(2026, envelope.Data!.CurrentYear);
        Assert.Equal(410, envelope.Data.MaximumScore);
        Assert.Equal(["Science", "Mathematics", "Literature"], envelope.Data.Tracks);
        Assert.True(envelope.Data.StudentResultsAvailable);
    }

    [Fact]
    public async Task Predict_returns_closest_cutoff_first_with_pagination()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admission/predict",
            new PredictRequestDto("Science", 380, Page: 1, PageSize: 1));

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PredictResponseDto>>();

        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal(2, envelope.Data!.TotalCount);
        Assert.Single(envelope.Data.Results);
        Assert.True(envelope.Data.HasMore);
        Assert.Equal("صيدلة", envelope.Data.Results[0].Faculty.NameAr);
    }

    [Fact]
    public async Task Predict_mathematics_returns_engineering_only()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admission/predict",
            new PredictRequestDto("Mathematics", 380));

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PredictResponseDto>>();

        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Single(envelope.Data!.Results);
        Assert.Equal("هندسة", envelope.Data.Results[0].Faculty.NameAr);
    }

    [Fact]
    public async Task Predict_rejects_score_above_maximum()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/admission/predict",
            new PredictRequestDto("Science", 500));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(ApiErrorCodes.ScoreExceedsMax, json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ThanaweyaResult_returns_maximum_score_with_result()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/thanaweya-results/2410001");

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<StudentResultDto>>();

        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal(410, envelope.Data!.MaximumScore);
        Assert.Equal("2410001", envelope.Data.SeatingNo);
    }

    [Fact]
    public async Task Admin_admission_years_requires_authentication()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/admission-years");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Thanaweya_lookup_rate_limit_uses_cloudflare_client_ip()
    {
        var client = _factory.CreateClient();
        const string clientIp = "203.0.113.10";

        for (var i = 1; i <= 60; i++)
        {
            using var request = LookupRequest(clientIp, $"198.51.100.{i}");
            using var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var blockedRequest = LookupRequest(clientIp, "198.51.100.250");
        using var blocked = await client.SendAsync(blockedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        using var blockedJson = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.RateLimitExceeded, blockedJson.RootElement.GetProperty("errorCode").GetString());

        using var otherRequest = LookupRequest("203.0.113.11", "198.51.100.251");
        using var other = await client.SendAsync(otherRequest);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    private static HttpRequestMessage LookupRequest(string cloudflareClientIp, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/thanaweya-results/2410001");
        request.Headers.TryAddWithoutValidation("CF-Connecting-IP", cloudflareClientIp);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task Student_results_import_requires_authentication()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([]), "file", "test.xlsx");

        var response = await client.PostAsync(
            "/api/admin/admission-years/1/student-results/import",
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

}
