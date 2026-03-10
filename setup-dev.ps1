# Nadosh Development Setup Script
# Run this after cloning the repository

Write-Host "🚀 Setting up Nadosh Development Environment..." -ForegroundColor Cyan

# Check prerequisites
Write-Host "`n1️⃣ Checking prerequisites..." -ForegroundColor Yellow

$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ .NET SDK installed: $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ .NET SDK not found. Please install .NET 10 SDK" -ForegroundColor Red
    exit 1
}

$dockerVersion = docker --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Docker installed: $dockerVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ Docker not found. Please install Docker Desktop" -ForegroundColor Red
    exit 1
}

# Create development settings
Write-Host "`n2️⃣ Setting up configuration files..." -ForegroundColor Yellow

if (-not (Test-Path "Nadosh.Api/appsettings.Development.json")) {
    Write-Host "   📝 Creating Nadosh.Api/appsettings.Development.json from template..." -ForegroundColor Cyan
    Copy-Item "Nadosh.Api/appsettings.Development.json.template" "Nadosh.Api/appsettings.Development.json"
    Write-Host "   ⚠️  Please update Nadosh.Api/appsettings.Development.json with your settings" -ForegroundColor Yellow
} else {
    Write-Host "   ✅ Development settings already exist" -ForegroundColor Green
}

# Restore NuGet packages
Write-Host "`n3️⃣ Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Packages restored successfully" -ForegroundColor Green
} else {
    Write-Host "   ❌ Failed to restore packages" -ForegroundColor Red
    exit 1
}

# Build solution
Write-Host "`n4️⃣ Building solution..." -ForegroundColor Yellow
dotnet build --configuration Debug
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Build successful" -ForegroundColor Green
} else {
    Write-Host "   ❌ Build failed" -ForegroundColor Red
    exit 1
}

# Start Docker services
Write-Host "`n5️⃣ Starting Docker services..." -ForegroundColor Yellow
docker compose up -d postgres redis
Start-Sleep -Seconds 5

# Run database migrations
Write-Host "`n6️⃣ Applying database migrations..." -ForegroundColor Yellow
dotnet ef database update --project Nadosh.Infrastructure --startup-project Nadosh.Api
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Migrations applied successfully" -ForegroundColor Green
} else {
    Write-Host "   ❌ Migration failed" -ForegroundColor Red
}

# Summary
Write-Host "`n✅ Development environment setup complete!" -ForegroundColor Green
Write-Host "`n📋 Next steps:" -ForegroundColor Cyan
Write-Host "   1. Update Nadosh.Api/appsettings.Development.json with your settings"
Write-Host "   2. Start the platform: docker compose up -d"
Write-Host "   3. Initialize demo data: curl -X POST http://localhost:5000/v1/targets/demo-scan -H 'X-API-Key: dev-api-key-123'"
Write-Host "   4. Open dashboard: http://localhost:5000"
Write-Host "`n📚 Documentation: See README.md and CONTRIBUTING.md"
Write-Host "🐛 Issues: https://github.com/YOUR_USERNAME/nadosh/issues`n"
