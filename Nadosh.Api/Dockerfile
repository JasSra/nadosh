FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Nadosh.Api/Nadosh.Api.csproj", "Nadosh.Api/"]
COPY ["Nadosh.Core/Nadosh.Core.csproj", "Nadosh.Core/"]
COPY ["Nadosh.Infrastructure/Nadosh.Infrastructure.csproj", "Nadosh.Infrastructure/"]
RUN dotnet restore "Nadosh.Api/Nadosh.Api.csproj"
COPY . .
WORKDIR "/src/Nadosh.Api"
RUN dotnet build "Nadosh.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Nadosh.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Nadosh.Api.dll"]
