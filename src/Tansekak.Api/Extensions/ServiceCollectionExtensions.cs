using System.Threading.RateLimiting;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tansekak.Api.Middleware;
using Tansekak.Application.Common;

namespace Tansekak.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = 1;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                var response = ApiResponse<object>.Fail(ApiErrorCodes.RateLimitExceeded);
                await context.HttpContext.Response.WriteAsJsonAsync(response);
            };

            options.AddPolicy("ThanaweyaLookup", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        services.Configure<TansekakOptions>(configuration.GetSection(TansekakOptions.SectionName));
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError
                        {
                            Field = entry.Key,
                            Message = error.ErrorMessage
                        }))
                        .ToList();

                    var message = errors.FirstOrDefault()?.Message
                        ?? ArabicErrorCatalog.GetMessage(ApiErrorCodes.ValidationFailed);
                    var response = ApiResponse<object>.Fail(ApiErrorCodes.ValidationFailed, message, errors);
                    return new BadRequestObjectResult(response);
                };
            });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
        services.AddFluentValidationAutoValidation();
        services.AddOpenApi();

        var frontendOrigin = configuration["Frontend:Origin"];
        if (!string.IsNullOrWhiteSpace(frontendOrigin))
        {
            services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                    policy.WithOrigins(frontendOrigin)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials());
            });
        }

        return services;
    }
}
