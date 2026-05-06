$dllPath = 'D:\JetBrainsRider\Projeler\CalibraHub\src\CalibraHub.Persistence\bin\Release\net10.0\CalibraHub.Persistence.dll'
$bytes = [System.IO.File]::ReadAllBytes($dllPath)
$utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
$utf8  = [System.Text.Encoding]::UTF8.GetString($bytes)

Write-Host "UTF-16 search:" -ForegroundColor Cyan
if ($utf16 -match 'group_id\]\s*UNIQUEIDENTIFIER') {
    Write-Host '  BAD: UTF-16 has UNIQUEIDENTIFIER for group_id' -ForegroundColor Red
}
if ($utf16 -match 'group_id\]\s*INT') {
    Write-Host '  GOOD: UTF-16 has INT for group_id' -ForegroundColor Green
}

Write-Host "UTF-8 search:" -ForegroundColor Cyan
if ($utf8 -match 'group_id\]\s*UNIQUEIDENTIFIER') {
    Write-Host '  BAD: UTF-8 has UNIQUEIDENTIFIER for group_id' -ForegroundColor Red
}
if ($utf8 -match 'group_id\]\s*INT') {
    Write-Host '  GOOD: UTF-8 has INT for group_id' -ForegroundColor Green
}

Write-Host "Looking for 'fk_material_card_field_settings_group_id' string:" -ForegroundColor Cyan
$idx = $utf16.IndexOf('fk_material_card_field_settings_group_id')
if ($idx -gt 0) {
    Write-Host "  Found at offset $idx, context (200 chars before):" -ForegroundColor Yellow
    $start = [Math]::Max(0, $idx - 200)
    Write-Host "  $($utf16.Substring($start, [Math]::Min(400, $utf16.Length - $start)))" -ForegroundColor Gray
}
