FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Proje dosyalar�n� (.csproj) cache mekanizmas�ndan yararlanmak i�in kopyal�yoruz
COPY ["LinkShortener.API/LinkShortener.API.csproj", "LinkShortener.API/"]
COPY ["LinkShortener.Application/LinkShortener.Application.csproj", "LinkShortener.Application/"]
COPY ["LinkShortener.Domain/LinkShortener.Domain.csproj", "LinkShortener.Domain/"]
COPY ["LinkShortener.Infrastructure/LinkShortener.Infrastructure.csproj", "LinkShortener.Infrastructure/"]

# Restore i�lemini tetikliyoruz
RUN dotnet restore "LinkShortener.API/LinkShortener.API.csproj"

# Kalan t�m kaynak kodlar� kopyal�yoruz
COPY . .
WORKDIR "/src/LinkShortener.API"
RUN dotnet build "LinkShortener.API.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "LinkShortener.API.csproj" -c Debug -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LinkShortener.API.dll"]