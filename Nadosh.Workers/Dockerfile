FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Nadosh.Workers/Nadosh.Workers.csproj", "Nadosh.Workers/"]
COPY ["Nadosh.Core/Nadosh.Core.csproj", "Nadosh.Core/"]
COPY ["Nadosh.Infrastructure/Nadosh.Infrastructure.csproj", "Nadosh.Infrastructure/"]
RUN dotnet restore "Nadosh.Workers/Nadosh.Workers.csproj"
COPY . .
WORKDIR "/src/Nadosh.Workers"
RUN dotnet build "Nadosh.Workers.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Nadosh.Workers.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Nadosh.Workers.dll"]
