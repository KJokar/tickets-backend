using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ViewModels.Common;

namespace TicketingApi.ExceptionHandling;

public static class ApiResultExceptionHandler
{
    private sealed class ExceptionHandler;

    public static IApplicationBuilder UseApiResultExceptionHandler(
        this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                IExceptionHandlerFeature? feature =
                    context.Features.Get<IExceptionHandlerFeature>();

                if (feature == null)
                    return;

                if (feature.Error is DbUpdateConcurrencyException)
                {
                    context.Response.StatusCode = 409;
                    await context.Response.WriteAsJsonAsync(ApiResultDto<object>.Failure(
                        "The ticket changed concurrently. Reload it and retry.", 409), context.RequestAborted);
                    return;
                }

                ILogger<ExceptionHandler> logger = context.RequestServices
                    .GetRequiredService<ILogger<ExceptionHandler>>();

                logger.LogError(
                    feature.Error,
                    "[{ExceptionType}] at [{Route}] due to [{Reason}]",
                    feature.Error.GetType().Name,
                    feature.Endpoint?.DisplayName,
                    feature.Error.Message);

                context.Response.StatusCode =
                    (int)HttpStatusCode.InternalServerError;

                await context.Response.WriteAsJsonAsync(
                    ApiResultDto<object>.Failure(
                        "An unexpected error occurred.",
                        (int)HttpStatusCode.InternalServerError),
                    context.RequestAborted);
            });
        });

        return app;
    }
}
