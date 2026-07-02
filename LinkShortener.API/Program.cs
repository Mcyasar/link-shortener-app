using LinkShortener.Application;
using LinkShortener.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

// 1. Katmanlar�n Ba��ml�l�k Enjeksiyonlar�n� (DI) Ba�l�yoruz
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddBackgroundWorkerServices();

builder.Services.AddControllers();

builder.Services.AddLogging();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder.WithOrigins("http://localhost:3000") // React/Vue/Angular uygulamanızın çalıştığı adres
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials()); // HTTP-only çerezler için gerekli
});

// Swagger/OpenAPI Deste�i (.NET 9.0 yerle�ik OpenAPI deste�i)
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        // JWT Bearer G�venlik �emas�n� OpenAPI 3.1 standartlar�na uygun tan�ml�yoruz
        var securityScheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT Token de�erinizi ba��na 'Bearer ' koyarak yaz�n�z. �rnek: 'Bearer eyJhbGciOi...'"
        };

        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = securityScheme;

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = JwtBearerDefaults.AuthenticationScheme
                }
            }] = Array.Empty<string>()
        };

        document.SecurityRequirements = new List<OpenApiSecurityRequirement> { requirement };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseRouting();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Link Shortener API")
               .WithOpenApiRoutePattern("/openapi/v1.json");

        // 🚀 Derleme hatasını çözen kurumsal yaklaşım:
        // options.Servers =
        // [
        //     new("http://linkshortener.local", "Lokal Kubernetes Girişi")
        // ];
    });

    await app.EnsureShortenedLinksTableCreatedAsync();
    await app.EnsureUserRefreshTokensTableCreatedAsync();
}


app.UseCors("AllowSpecificOrigin"); // CORS middleware'ini UseAuthentication'dan önce ekliyoruz

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();


if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();