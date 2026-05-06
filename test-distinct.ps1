$base = 'http://localhost:61001'

Write-Host "=== Tum Rehberler ===" -ForegroundColor Cyan
try {
    $guides = Invoke-RestMethod "$base/api/guides" -UseBasicParsing -TimeoutSec 5
    $guides | Where-Object { $_.viewName -like '*Contact*' -or $_.guideCode -like '*Contact*' } |
        Select-Object guideCode, viewName, gridColumnsJson |
        Format-List
} catch {
    Write-Host "API hatasi: $($_.Exception.Message)" -ForegroundColor Red
    return
}

Write-Host "`n=== Contacts Distinct: City ===" -ForegroundColor Cyan
try {
    $values = Invoke-RestMethod "$base/api/guides/Contacts/distinct/City" -UseBasicParsing -TimeoutSec 5
    Write-Host "Toplam: $($values.Count) deger" -ForegroundColor Yellow
    $values | ForEach-Object { Write-Host "  - $_" }
} catch {
    Write-Host "Distinct hatasi: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Contacts Schema (kolonlar) ===" -ForegroundColor Cyan
try {
    $schema = Invoke-RestMethod "$base/api/guides/Contacts/schema" -UseBasicParsing -TimeoutSec 5
    Write-Host "Kolonlar:" -ForegroundColor Yellow
    $schema.columns | ForEach-Object { Write-Host "  - $_" }
} catch {
    Write-Host "Schema hatasi: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== /api/guides/views (raw view list) ===" -ForegroundColor Cyan
try {
    $views = Invoke-RestMethod "$base/api/guides/views" -UseBasicParsing -TimeoutSec 5
    $contactView = $views | Where-Object { $_.viewName -like '*Contact*' }
    if ($contactView) {
        Write-Host "View adi: $($contactView.viewName)"
        Write-Host "Kolonlar: $($contactView.columns -join ', ')"
    } else {
        Write-Host "Contacts view bulunamadi" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Views hatasi: $($_.Exception.Message)" -ForegroundColor Red
}
