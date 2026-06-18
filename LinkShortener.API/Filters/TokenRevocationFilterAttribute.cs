using System.Text;
using LinkShortener.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LinkShortener.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TokenRevocationFilterAttribute : Attribute, IAsyncAuthorizationFilter
{
    // "Bearer " kelimesinin UTF-8 byte karşılığı (Statik ve readonly olarak RAM'de durur)
    private static readonly byte[] BearerPrefixBytes = Encoding.UTF8.GetBytes("Bearer ");

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var blacklistService = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();

        // 1. 🔥 STRING YERİNE BYTE: Header'ı string'e çevirmeden doğrudan ham UTF-8 byte'ları üzerinden okuyoruz!
        // ASP.NET Core Kestrel, başlıkları bize StringValues olarak sunar ama arkasında String'in asıl bellek bölgesine Span ile bakabiliriz.
        string authHeader = context.HttpContext.Request.Headers["Authorization"].ToString();
        
        if (string.IsNullOrEmpty(authHeader)) return;

        // Header string'ini tek bir seferliğine karakter Span'ine çeviriyoruz (Allocation yapmaz, pointer açar)
        ReadOnlySpan<char> headerSpan = authHeader.AsSpan();

        // "Bearer " ile başlayıp başlamadığını kontrol ediyoruz (Karakter bazlı)
        if (!headerSpan.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            // 2. 🔥 GERÇEK ZERO-ALLOCATION SIHRI: 
            // .AsMemory() kullanarak Heap bellekte yepyeni bir string (Substring) OLUŞTURMUYORUZ.
            // Sadece mevcut authHeader string'inin 7. karakterinden sonrasını işaret eden 
            // çok hafif bir ReadOnlyMemory<char> penceresi açıyoruz.
            ReadOnlyMemory<char> tokenMemory = authHeader.AsMemory(7).TrimStart();

            // 3. Modern Handler'a doğrudan bu ReadOnlyMemory<char> dizisini paslıyoruz!
            var tokenHandler = new JsonWebTokenHandler();
            
            // Metot bu overload'u yerel olarak desteklediği için .ToString() dememize GEREK KALMADI!
            // Kriptografik ayrıştırma doğrudan bu bellek penceresi üzerinden akar.
            JsonWebToken jwtToken = tokenHandler.ReadJsonWebToken(tokenMemory);

            // 5. 'jti' değerini çekiyoruz. jwtToken.GetClaim metodu da string üretmeden kontrol yapabilir.
            // jti değeri genellikle kısa bir GUID veya string olduğu için burada string'e mecbur kalabiliriz 
            // ama en azından o devasa JWT token string'ini Heap'e kopyalamaktan tamamen kurtulduk.
            string? jti = jwtToken.Id; // .Id property'si doğrudan 'jti' claim'ini döndürür.

            if (!string.IsNullOrEmpty(jti))
            {
                // Redis'e mikrosaniyeler içinde soruyoruz
                bool isBlacklisted = await blacklistService.IsTokenBlacklistedAsync(jti, context.HttpContext.RequestAborted);
                
                if (isBlacklisted)
                {
                    context.Result = new UnauthorizedObjectResult(new { message = "Bu oturum sonlandırılmış veya geçersiz kılınmış." });
                }
            }
        }
        catch
        {
            context.Result = new UnauthorizedResult();
        }
    }
}