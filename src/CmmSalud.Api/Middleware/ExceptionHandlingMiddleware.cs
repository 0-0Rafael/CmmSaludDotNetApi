using System.Net;
using System.Text.Json;
using CmmSalud.Api.Common;

namespace CmmSalud.Api.Middleware;

public sealed class ExceptionHandlingMiddleware : IMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IWebHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        ILogger<ExceptionHandlingMiddleware> log,
        IWebHostEnvironment env)
    {
        _log = log;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "🔥 Unhandled exception");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            // ✅ En Development sí devolvemos el error real para debug
            if (_env.IsDevelopment())
            {
                var devPayload = new ApiResponse<object>(
                    500,
                    ex.Message,
                    new { detail = ex.ToString() }
                );

                await context.Response.WriteAsync(JsonSerializer.Serialize(devPayload));
                return;
            }

            // ✅ En producción, genérico
            var payload = new ApiResponse<object>(500, "Error interno del servidor.", null);
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
