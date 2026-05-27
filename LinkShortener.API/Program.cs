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

// 1. Katmanlarýn Baðýmlýlýk Enjeksiyonlarýný (DI) Baðlýyoruz
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddControllers();

// 2. Güvenlik: Rate Limiting (Hýz Sýnýrý) Ayarlarý
// API'mizin saniyede binlerce istek atan botlar tarafýndan çökertilmesini engelliyoruz
///// ---> Redis tabanlý rate limiter infra katmaný üzerinden eklendiði için bu kod parçasý commentlendi
//builder.Services.AddRateLimiter(options =>
//{
//    options.AddFixedWindowLimiter(policyName: "FixedWindowPolicy", fixedOptions =>
//    {
//        fixedOptions.PermitLimit = 10; // 60 saniyede maksimum 10 isteðe izin ver
//        fixedOptions.Window = TimeSpan.FromSeconds(60);
//        fixedOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
//        fixedOptions.QueueLimit = 2; // Kuyrukta bekleyebilecek maksimum istek
//    });

//    // Sýnýr aþýldýðýnda istemciye döneceðimiz yanýt
//    options.OnRejected = async (context, token) =>
//    {
//        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
//        await context.HttpContext.Response.WriteAsync("Çok fazla istek attýnýz. Lütfen biraz bekleyin.", cancellationToken: token);
//    };
//});

// Swagger/OpenAPI Desteði (.NET 9.0 yerleþik OpenAPI desteði)
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        // JWT Bearer Güvenlik Þemasýný OpenAPI 3.1 standartlarýna uygun tanýmlýyoruz
        var securityScheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT Token deðerinizi baþýna 'Bearer ' koyarak yazýnýz. Örnek: 'Bearer eyJhbGciOi...'"
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
               .WithOpenApiRoutePattern("/openapi/v1.json"); // Þemanýn çekileceði tam adres
    });

    await app.EnsureTablesCreatedAsync();
}

// Rate Limiter Middleware'ini aktif ediyoruz
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.UseHttpsRedirection();


// --- API ENDPOINTS (Minimal APIs) ---

// 1. UÇ: Link Kýsaltma (Write/Command) - Rate Limiting Korumalý
//app.MapPost("/api/links", async (CreateShortLinkCommand command, IMediator mediator) =>
//{
//    try
//    {
//        // MediatR vasýtasýyla isteði Application katmanýndaki Handler'a fýrlatýyoruz
//        var shortCode = await mediator.Send(command);
//        return Results.Ok(new { ShortCode = shortCode, ShortUrl = $"/r/{shortCode}" });
//    }
//    catch (ArgumentException ex)
//    {
//        return Results.BadRequest(new { Message = ex.Message });
//    }
//})
//.WithName("CreateShortLink")
//.RequireRateLimiting("FixedWindowPolicy"); // Politikamýzý baðladýk


//// 2. UÇ: Link Yönlendirme (Read/Query) - Sistemdeki en yüksek performanslý yer!
//app.MapGet("/r/{shortCode}", async (string shortCode, IMediator mediator) =>
//{
//    try
//    {
//        // Önce Cache'e, yoksa DynamoDB'ye giden sorguyu tetikliyoruz
//        var originalUrl = await mediator.Send(new GetOriginalLinkQuery(shortCode));

//        // Kullanýcýyý 302 (Found) geçici yönlendirmesiyle orijinal siteye uçuruyoruz
//        return Results.Redirect(originalUrl, permanent: false);
//    }
//    catch (KeyNotFoundException)
//    {
//        return Results.NotFound(new { Message = "Böyle bir kýsa link bulunamadý." });
//    }
//    catch (InvalidOperationException ex)
//    {
//        return Results.BadRequest(new { Message = ex.Message });
//    }
//})
//.WithName("RedirectToOriginalUrl");

app.MapControllers();

app.Run();