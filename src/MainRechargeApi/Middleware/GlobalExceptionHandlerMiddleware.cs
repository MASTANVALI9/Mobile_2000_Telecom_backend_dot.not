// Global exception handling middleware to catch unhandled errors and return structured JSON responses
using System.Net;
using System.Text.Json;
using MainRechargeApi.DTOs;

namespace MainRechargeApi.Middleware
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Log without logging passwords or credentials
            _logger.LogError(
                exception,
                "[GLOBAL_EXCEPTION] Unhandled exception occurred processing {Method} {Path}: {Message}",
                context.Request.Method,
                context.Request.Path,
                exception.Message
            );

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            string errorCode = "INTERNAL_SERVER_ERROR";
            string userFriendlyMessage = "An unexpected error occurred while processing your request.";

            if (exception is Microsoft.Data.SqlClient.SqlException || exception is Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                errorCode = "DATABASE_ERROR";
                userFriendlyMessage = "A database error occurred while processing your request. Please try again later.";
            }

            var errorResponse = new ApiErrorResponse(
                context.Response.StatusCode,
                errorCode,
                userFriendlyMessage
            );

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
