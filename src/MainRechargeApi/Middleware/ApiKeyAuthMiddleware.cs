using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MainRechargeApi.DTOs;

namespace MainRechargeApi.Middleware
{
    public class ApiKeyAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiKeyAuthMiddleware> _logger;

        public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyAuthMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            // Skip auth for Swagger documentation and root endpoint
            if (path.StartsWith("/swagger") || path == "/" || path == "/favicon.ico")
            {
                await _next(context);
                return;
            }

            string headerName = _configuration["Authentication:HeaderName"] ?? "X-API-Key";
            string? configuredApiKey = _configuration["Authentication:ApiKey"];

            if (string.IsNullOrWhiteSpace(configuredApiKey))
            {
                _logger.LogWarning("[AUTH] No API key configured in application settings. Allowing request.");
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(headerName, out var extractedApiKeyValues) ||
                string.IsNullOrWhiteSpace(extractedApiKeyValues.FirstOrDefault()))
            {
                _logger.LogWarning("[AUTH_FAILURE] Authentication failed: Missing header '{HeaderName}' for path {Path}.", headerName, context.Request.Path);
                await WriteUnauthorizedResponseAsync(context, $"Missing '{headerName}' authentication header.");
                return;
            }

            string providedKey = extractedApiKeyValues.First()!;

            // Constant-time string comparison to prevent timing attacks
            byte[] providedBytes = Encoding.UTF8.GetBytes(providedKey);
            byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);

            bool isValid = providedBytes.Length == configuredBytes.Length &&
                          CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);

            if (!isValid)
            {
                _logger.LogWarning("[AUTH_FAILURE] Authentication failed: Invalid API key provided for path {Path}.", context.Request.Path);
                await WriteUnauthorizedResponseAsync(context, "Invalid API key.");
                return;
            }

            await _next(context);
        }

        private static async Task WriteUnauthorizedResponseAsync(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var errorResponse = new ApiErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTHENTICATION_FAILED",
                message
            );

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
