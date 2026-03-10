#!/usr/bin/env pwsh
# Download Wireshark's MAC vendor database (manuf file)
# This database maps IEEE OUI prefixes to vendor/manufacturer names

$ManufUrl = "https://www.wireshark.org/download/automated/data/manuf"
$OutputPath = Join-Path $PSScriptRoot "manuf"

Write-Host "Downloading Wireshark MAC vendor database..." -ForegroundColor Cyan
Write-Host "  URL: $ManufUrl" -ForegroundColor Gray
Write-Host "  Output: $OutputPath" -ForegroundColor Gray

try {
    Invoke-WebRequest -Uri $ManufUrl -OutFile $OutputPath -UseBasicParsing
    
    $fileSize = (Get-Item $OutputPath).Length / 1KB
    $lineCount = (Get-Content $OutputPath).Count
    
    Write-Host "`n✓ Download complete!" -ForegroundColor Green
    Write-Host "  Size: $([math]::Round($fileSize, 2)) KB" -ForegroundColor Gray
    Write-Host "  Lines: $lineCount" -ForegroundColor Gray
    
    # Show a sample of vendor entries
    Write-Host "`nSample vendor entries:" -ForegroundColor Cyan
    Get-Content $OutputPath | Where-Object { $_ -notmatch '^#' -and $_ -match '\S' } | Select-Object -First 5 | ForEach-Object {
        Write-Host "  $_" -ForegroundColor Gray
    }
    
    Write-Host "`nDatabase is ready for use!" -ForegroundColor Green
    Write-Host "Place this file in the same directory as your application binaries." -ForegroundColor Yellow
}
catch {
    Write-Host "`n✗ Error downloading database: $_" -ForegroundColor Red
    exit 1
}
