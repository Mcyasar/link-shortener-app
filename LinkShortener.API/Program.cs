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

// 2. G�venlik: Rate Limiting (H�z S�n�r�) Ayarlar�
// API'mizin saniyede binlerce istek atan botlar taraf�ndan ��kertilmesini engelliyoruz
///// ---> Redis tabanl� rate limiter infra katman� �zerinden eklendi�i i�in bu kod par�as� commentlendi
//builder.Services.AddRateLimiter(options =>
//{
//    options.AddFixedWindowLimiter(policyName: "FixedWindowPolicy", fixedOptions =>
//    {
//        fixedOptions.PermitLimit = 10; // 60 saniyede maksimum 10 iste�e izin ver
//        fixedOptions.Window = TimeSpan.FromSeconds(60);
//        fixedOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
//        fixedOptions.QueueLimit = 2; // Kuyrukta bekleyebilecek maksimum istek
//    });

//    // S�n�r a��ld���nda istemciye d�nece�imiz yan�t
//    options.OnRejected = async (context, token) =>
//    {
//        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
//        await context.HttpContext.Response.WriteAsync("�ok fazla istek att�n�z. L�tfen biraz bekleyin.", cancellationToken: token);
//    };
//});

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
        options.Servers =
        [
            new("http://linkshortener.local", "Lokal Kubernetes Girişi")
        ];
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


// --- API ENDPOINTS (Minimal APIs) ---

// 1. U�: Link K�saltma (Write/Command) - Rate Limiting Korumal�
//app.MapPost("/api/links", async (CreateShortLinkCommand command, IMediator mediator) =>
//{
//    try
//    {
//        // MediatR vas�tas�yla iste�i Application katman�ndaki Handler'a f�rlat�yoruz
//        var shortCode = await mediator.Send(command);
//        return Results.Ok(new { ShortCode = shortCode, ShortUrl = $"/r/{shortCode}" });
//    }
//    catch (ArgumentException ex)
//    {
//        return Results.BadRequest(new { Message = ex.Message });
//    }
//})
//.WithName("CreateShortLink")
//.RequireRateLimiting("FixedWindowPolicy"); // Politikam�z� ba�lad�k


//// 2. U�: Link Y�nlendirme (Read/Query) - Sistemdeki en y�ksek performansl� yer!
//app.MapGet("/r/{shortCode}", async (string shortCode, IMediator mediator) =>
//{
//    try
//    {
//        // �nce Cache'e, yoksa DynamoDB'ye giden sorguyu tetikliyoruz
//        var originalUrl = await mediator.Send(new GetOriginalLinkQuery(shortCode));

//        // Kullan�c�y� 302 (Found) ge�ici y�nlendirmesiyle orijinal siteye u�uruyoruz
//        return Results.Redirect(originalUrl, permanent: false);
//    }
//    catch (KeyNotFoundException)
//    {
//        return Results.NotFound(new { Message = "B�yle bir k�sa link bulunamad�." });
//    }
//    catch (InvalidOperationException ex)
//    {
//        return Results.BadRequest(new { Message = ex.Message });
//    }
//})
//.WithName("RedirectToOriginalUrl");

app.MapControllers();

app.Run();