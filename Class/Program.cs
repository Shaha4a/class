using ClassIn.Hubs;
using ClassIn.Infrastructure.Data;
using ClassIn.Infrastructure.Extensions;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

var envConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
var configConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString = string.IsNullOrWhiteSpace(envConnectionString) ? configConnectionString : envConnectionString;

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string is missing. Set CONNECTION_STRING in .env or ConnectionStrings:DefaultConnection.");
}

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("MvpCors", policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials();
    });
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in appsettings.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ClassInMvp";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ClassInClient";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/class"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

await DatabaseInitializer.EnsureCreatedAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("MvpCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ClassHub>("/hubs/class");

app.Run("http://0.0.0.0:4011");

