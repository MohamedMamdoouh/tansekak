using Tansekak.Api.Middleware;

namespace Tansekak.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication ConfigureApiMiddleware(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<GlobalExceptionHandler>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        else
        {
            app.UseHsts();
        }

        var frontendOrigin = app.Configuration["Frontend:Origin"];
        if (!string.IsNullOrWhiteSpace(frontendOrigin))
        {
            app.UseCors("Frontend");
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapControllers();
        app.MapFallbackToFile("index.html");

        return app;
    }
}
