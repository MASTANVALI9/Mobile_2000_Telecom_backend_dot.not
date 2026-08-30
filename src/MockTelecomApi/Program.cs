using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MockTelecomApi.Data;
using MockTelecomApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Telecom Recharge Platform - Mock Telecom Provider API",
        Version = "v1",
        Description = "Simulated telecom provider API for testing recharge workflows, latencies, errors, and timeouts."
    });

    var apiKeyHeader = builder.Configuration["Authentication:HeaderName"] ?? "X-Provider-API-Key";

    c.AddSecurityDefinition("ProviderApiKey", new OpenApiSecurityScheme
    {
        Description = $"Telecom Provider API Key authentication using the '{apiKeyHeader}' header. Example: 'telecom-provider-test-key-2026'",
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
                    Id = "ProviderApiKey"
                },
                Scheme = "ApiKeyScheme",
                Name = apiKeyHeader,
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

builder.Services.AddDbContext<MockDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MockDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mock Telecom Provider API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.Run();
