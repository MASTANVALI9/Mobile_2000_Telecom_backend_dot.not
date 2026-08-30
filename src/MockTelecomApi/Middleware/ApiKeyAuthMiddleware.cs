using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MockTelecomApi.Middleware
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

            // Allow Swagger and rules endpoints without authentication
            if (path.StartsWith("/swagger") || path == "/" || path == "/favicon.ico" || path == "/api/provider/recharge/rules")
            {
                await _next(context);
                return;
            }

            string headerName = _configuration["Authentication:HeaderName"] ?? "X-Provider-API-Key";
            string? configuredApiKey = _configuration["Authentication:ApiKey"];

            if (string.IsNullOrWhiteSpace(configuredApiKey))
            {
                _logger.LogWarning("[AUTH] No provider API key configured in application settings. Allowing request.");
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue(headerName, out var extractedApiKeyValues) ||
                string.IsNullOrWhiteSpace(extractedApiKeyValues.FirstOrDefault()))
            {
                // NEVER log the received credentials
                _logger.LogWarning("[AUTH_FAILURE] Provider authentication failed: Missing header '{HeaderName}' for path {Path}.", headerName, context.Request.Path);
                await WriteUnauthorizedResponseAsync(context, $"Missing '{headerName}' authentication header.");
                return;
            }

            string providedKey = extractedApiKeyValues.First()!;

            byte[] providedBytes = Encoding.UTF8.GetBytes(providedKey);
            byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);

            bool isValid = providedBytes.Length == configuredBytes.Length &&
                          CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);

            if (!isValid)
            {
                _logger.LogWarning("[AUTH_FAILURE] Provider authentication failed: Invalid API key provided for path {Path}.", context.Request.Path);
                await WriteUnauthorizedResponseAsync(context, "Invalid provider API key.");
                return;
            }

            await _next(context);
        }

        private static async Task WriteUnauthorizedResponseAsync(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var errorPayload = new
            {
                statusCode = StatusCodes.Status401Unauthorized,
                error = "AUTHENTICATION_FAILED",
                message = message,
                timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
