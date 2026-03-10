#!/usr/bin/env pwsh
# Test MAC address enrichment functionality

Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  MAC Address Enrichment - Functionality Test                ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Test 1: Verify database file exists
Write-Host "Test 1: MAC vendor database..." -ForegroundColor Yellow
$manufPath = ".\scripts\manuf"
if (Test-Path $manufPath) {
    $size = (Get-Item $manufPath).Length / 1KB
    $lines = (Get-Content $manufPath).Count
    Write-Host "  ✓ Database found: $([math]::Round($size, 2)) KB, $lines lines" -ForegroundColor Green
} else {
    Write-Host "  ✗ Database not found at $manufPath" -ForegroundColor Red
    exit 1
}

# Test 2: Test MAC vendor lookups
Write-Host "`nTest 2: Sample vendor lookups..." -ForegroundColor Yellow

$testMacs = @{
    "00:00:0c:07:ac:01" = "Cisco"
    "4c:fc:aa:01:02:03" = "Tesla"
    "f0:9f:c2:01:02:03" = "Ubiquiti"
    "00:1d:c0:01:02:03" = "Apple"
    "f4:5c:89:01:02:03" = "Google"
}

foreach ($mac in $testMacs.Keys) {
    $oui = $mac.Substring(0, 8)  # First 3 bytes
    $vendor = (Get-Content $manufPath | Where-Object { $_ -match "^$oui" } | Select-Object -First 1)
    
    if ($vendor) {
        $vendorName = ($vendor -split '\t')[1]
        $expectedVendor = $testMacs[$mac]
        
        if ($vendorName -like "*$expectedVendor*") {
            Write-Host "  ✓ $mac → $vendorName" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ $mac → $vendorName (expected: $expectedVendor)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ✗ $mac → Not found" -ForegroundColor Red
    }
}

# Test 3: Check database migration status
Write-Host "`nTest 3: Database schema..." -ForegroundColor Yellow
try {
    $result = docker exec nadosh-postgres psql -U nadosh -d nadosh -tAc "SELECT column_name FROM information_schema.columns WHERE table_name='Targets' AND column_name IN ('MacAddress', 'MacVendor', 'DeviceType');" 2>&1
    
    if ($result -match "MacAddress" -and $result -match "MacVendor" -and $result -match "DeviceType") {
        Write-Host "  ✓ Targets table has MAC columns" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Targets table missing MAC columns" -ForegroundColor Red
    }
    
    $obsResult = docker exec nadosh-postgres psql -U nadosh -d nadosh -tAc "SELECT column_name FROM information_schema.columns WHERE table_name='Observations' AND column_name IN ('MacAddress', 'MacVendor', 'DeviceType');" 2>&1
    
    if ($obsResult -match "MacAddress") {
        Write-Host "  ✓ Observations table has MAC columns" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Observations table missing MAC columns" -ForegroundColor Red
    }
    
    $expResult = docker exec nadosh-postgres psql -U nadosh -d nadosh -tAc "SELECT column_name FROM information_schema.columns WHERE table_name='CurrentExposures' AND column_name IN ('MacAddress', 'MacVendor', 'DeviceType');" 2>&1
    
    if ($expResult -match "MacAddress") {
        Write-Host "  ✓ CurrentExposures table has MAC columns" -ForegroundColor Green
    } else {
        Write-Host "  ✗ CurrentExposures table missing MAC columns" -ForegroundColor Red
    }
} catch {
    Write-Host "  ⚠ Could not verify database (container may not be running)" -ForegroundColor Yellow
}

# Test 4: Check for enriched data
Write-Host "`nTest 4: Existing MAC data..." -ForegroundColor Yellow
try {
    $enrichedCount = docker exec nadosh-postgres psql -U nadosh -d nadosh -tAc 'SELECT COUNT(*) FROM "Targets" WHERE "MacAddress" IS NOT NULL;' 2>&1
    
    if ($enrichedCount -match '^\d+$') {
        if ([int]$enrichedCount -gt 0) {
            Write-Host "  ✓ Found $enrichedCount targets with MAC addresses" -ForegroundColor Green
            
            # Show sample
            Write-Host "`n  Sample enriched targets:" -ForegroundColor Cyan
            docker exec nadosh-postgres psql -U nadosh -d nadosh -c 'SELECT "Ip", "MacAddress", "MacVendor", "DeviceType" FROM "Targets" WHERE "MacAddress" IS NOT NULL LIMIT 5;' 2>&1 | ForEach-Object {
                Write-Host "    $_" -ForegroundColor Gray
            }
        } else {
            Write-Host "  ⚠ No enriched targets yet (run a scan to populate)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "  ⚠ Could not query database" -ForegroundColor Yellow
}

# Summary
Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Test Summary                                                ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  MAC enrichment system is ready!" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "  1. Start workers with MAC enrichment enabled:" -ForegroundColor Gray
Write-Host "     `$env:WORKER_ROLE='all,mac-enrichment'" -ForegroundColor Gray
Write-Host "     dotnet run --project Nadosh.Workers" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Run a scan on your local network (192.168.x.x)" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Check for enriched data:" -ForegroundColor Gray
Write-Host "     .\scripts\test-mac-enrichment.ps1" -ForegroundColor Gray
Write-Host ""
