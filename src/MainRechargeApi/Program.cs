using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MainRechargeApi.Data;
using MainRechargeApi.Services;
using MainRechargeApi.Models;
using MainRechargeApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Telecom Recharge Platform - Main Recharge API",
        Version = "v1",
        Description = "Core API for processing mobile recharges across Jio, Airtel, Vi, and BSNL."
    });

    var apiKeyHeader = builder.Configuration["Authentication:HeaderName"] ?? "X-API-Key";

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = $"API Key authentication using the '{apiKeyHeader}' header. Example: 'mobile2000-secret-api-key-2026'",
        Type = SecuritySchemeType.ApiKey,
        Name = apiKeyHeader,
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                },
                Scheme = "ApiKeyScheme",
                Name = apiKeyHeader,
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

builder.Services.AddDbContext<RechargeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Timeout and default headers for provider API calls
builder.Services.AddHttpClient("ProviderApi", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    int timeoutSeconds = int.TryParse(config["ProviderApi:TimeoutSeconds"], out int t) ? t : 10;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

    string providerApiKeyHeader = config["ProviderApi:HeaderName"] ?? "X-Provider-API-Key";
    string? providerApiKey = config["ProviderApi:ApiKey"];
    if (!string.IsNullOrWhiteSpace(providerApiKey))
    {
        client.DefaultRequestHeaders.Add(providerApiKeyHeader, providerApiKey);
    }
});

builder.Services.AddScoped<ICardImportService, CardImportService>();
builder.Services.AddHostedService<ReconciliationWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RechargeDbContext>();
    dbContext.Database.EnsureCreated();

    if (!dbContext.TelecomOperators.Any())
    {
        dbContext.TelecomOperators.AddRange(
            new TelecomOperator { Name = "Jio", IsActive = true },
            new TelecomOperator { Name = "Airtel", IsActive = true },
            new TelecomOperator { Name = "Vi", IsActive = true },
            new TelecomOperator { Name = "BSNL", IsActive = true }
        );
        dbContext.SaveChanges();
    }
}

// Global exception handler must run first to catch downstream errors
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Main Recharge API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.Run();
