using LinkShortener.Application;
using LinkShortener.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using LinkShortener.API.Common;
using LinkShortener.Application.Common.Configurations;
using OpenTelemetry.Metrics;
using Microsoft.AspNetCore.Diagnostics;
using Polly.Timeout;
using Polly.CircuitBreaker;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;


var builder = WebApplication.CreateBuilder(args);

// Varsayılan düşük MinThreads değerini 500 VU yükü için önceden ısıtıyoruz
ThreadPool.GetMinThreads(out int currentMinWorker, out int currentMinCompletion);
ThreadPool.SetMinThreads(500, currentMinCompletion); // Worker thread alt limitini 500 yap

///TODO otel service url will be defined in the configuration file, not hardcoded here
var collectorUri = new Uri("http://otel-collector-service:4317");

builder.Services.Configure<TelemetrySettings>(builder.Configuration.GetSection("TelemetrySettings"));
var telemetrySettings = builder.Configuration.GetSection("TelemetrySettings").Get<TelemetrySettings>();
var serviceName = telemetrySettings?.ServiceName ?? StaticValues.ApiName;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

// 1. Katmanların Bağımlılık Enjeksiyonlarını (DI) Bağlıyoruz
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
//builder.Services.AddBackgroundWorkerServices();

builder.Services.AddControllers();

builder.Services.AddLogging();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: StaticValues.ApiVersion))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(serviceName)
            .AddAspNetCoreInstrumentation(options =>
            {
                // İsteğe bağlı: Sadece api rotalarını trace et, swagger/scalar isteklerini filtrele
                options.Filter = (httpContext) => httpContext.Request.Path.StartsWithSegments("/api");
            })
            .AddHttpClientInstrumentation();
            
            // 🚀 LOKAL GELİŞTİRME KONFORU: 
        // Eğer uygulama "Development" (Saf lokal) ortamındaysa Jaeger'a veri göndermeyi atla.
        // Sadece "Kubernetes" veya "Staging/Production" ortamındaysa Exporter'ı bağla.
        if (!builder.Environment.IsDevelopment())
        {
             tracing.AddOtlpExporter(options =>
            {
                // Kubernetes içindeki Jaeger servisimizin gRPC endpoint'ini gösteriyoruz
                options.Endpoint = collectorUri;
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
        }           
    }).WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation() // HTTP istek adetleri, süreleri vb. otomatik toplar
            .AddHttpClientInstrumentation() // Dışarı giden HTTP metrikleri
            .AddRuntimeInstrumentation()  // 🚀 GC (Garbage Collection), Thread Pool
            .AddProcessInstrumentation(); // 🚀 CPU ve RAM metriklerinin kilidini açar
            
        // 🚀 METRİKLER İÇİN OTLP EXPORTER
        if (!builder.Environment.IsDevelopment())
        {
            metrics.AddOtlpExporter(o => { o.Endpoint = collectorUri; o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc; });
        }

    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder.WithOrigins("http://localhost:3000") // React/Vue/Angular uygulamanızın çalıştığı adres
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials()); // HTTP-only çerezler için gerekli
});

// Swagger/OpenAPI Desteği (.NET 9.0 yerle�ik OpenAPI deste�i)
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

builder.Services.Configure<SocketTransportOptions>(options =>
{
    options.Backlog = 1024; // Gelen TCP bağlantı kuyruğunu genişletir
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);

    // KESTREL 5 SANİYE TIMEOUT KORUMASINI DEVRE DIŞI BIRAKIN
    options.Limits.MinRequestBodyDataRate = null;
    options.Limits.MinResponseDataRate = null;
});

var app = builder.Build();

app.UseRouting();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Link Shortener API")
               .WithOpenApiRoutePattern("/openapi/v1.json");

        #if !DEBUG //when debugging we need actual localhost:5188 and we need to prevent this url assignment below
        // 🚀 Derleme hatasını çözen kurumsal yaklaşım:
        options.Servers =
        [
            new("http://linkshortener.local", "Lokal Kubernetes Girişi")
        ];
        #endif
    });

    await app.EnsureShortenedLinksTableCreatedAsync();
    await app.EnsureUserRefreshTokensTableCreatedAsync();
}


app.UseCors("AllowSpecificOrigin"); // CORS middleware'ini UseAuthentication'dan önce ekliyoruz

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionHandlerFeature?.Error;

        if (exception != null)
        {
            // Logger servisini DI Container'dan alıyoruz
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            // 🚨 Hatayı StackTrace ile birlikte LogError olarak basıyoruz
            logger.LogError(exception, "İstek işlenirken bir hata oluştu. Path: {Path}, Exception: {Message}", 
                context.Request.Path, exception.Message);
        }

        if (exception is TimeoutRejectedException)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new { error = "İşlem zaman aşımına uğradı (Polly Timeout)." });
        }
        else if (exception is BrokenCircuitException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Devre kesici açık, servis geçici olarak kullanım dışı." });
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Bilinmeyen bir hata oluştu." });
        }
    });
});

app.Run();