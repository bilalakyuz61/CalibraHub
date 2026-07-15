<#
.SYNOPSIS
  DEV ortaminda CalibraHub Web / Worker'i Windows servisi olarak
  kaydeder + ACL grant uygular. ServiceManager UI'i UAC sormadan calistirir.

.DESCRIPTION
  Production'da bu islemi installer/CalibraHub.iss yapar. DEV'de denemek icin
  bu script tek atimda her seyi hallediyor:

    1) Calisan foreground process'leri durdurur:
       - port 61001'deki dotnet run → KULLANICI manuel durdurmali (kill ETMEYIZ
         cunku kullanicinin terminal'i)
    2) Windows servislerini olusturur (sc.exe create), env var ile port set eder
    3) grant-service-acl.ps1 calistirir → AU yetkisi
    4) Servisleri baslatir
    5) Durum raporunu basar

  ADMIN OLARAK CALISTIRILMALI.

.PARAMETER UninstallOnly
  Verilirse sadece servisleri durdurup siler (bu script ile yaratilanlari
  geri alir). Ardindan dev modda foreground calistirmaya donmek icin:
    dotnet run --project src\CalibraHub.Web

.EXAMPLE
  pwsh installer\dev-register-services.ps1
  # Tum servisleri kurar + baslatir + ACL grant eder

.EXAMPLE
  pwsh installer\dev-register-services.ps1 -UninstallOnly
  # Servisleri durdurup siler — dev moduna donmek icin
#>
param(
    [switch]$UninstallOnly
)

$ErrorActionPreference = "Stop"

# ── Admin kontrol ─────────────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[dev-register-services] HATA: Bu script ADMIN PowerShell'de calistirilmali." -ForegroundColor Red
    exit 1
}

$repoRoot       = Resolve-Path (Join-Path $PSScriptRoot "..")
$webExe         = Join-Path $repoRoot "src\CalibraHub.Web\bin\Debug\net10.0\CalibraHub.Web.exe"
$workerExe      = Join-Path $repoRoot "src\CalibraHub.Worker\bin\Debug\net10.0\CalibraHub.Worker.exe"
$grantScript    = Join-Path $PSScriptRoot "grant-service-acl.ps1"

$services = @(
    @{ Name = "CalibraHub Web";     Exe = $webExe;     Display = "CalibraHub Web";     Description = "DEV — ASP.NET Core web (port 61001)";     Args = ""; Env = "ASPNETCORE_URLS=http://localhost:61001`0ASPNETCORE_ENVIRONMENT=Development" }
    @{ Name = "CalibraHub Worker";  Exe = $workerExe;  Display = "CalibraHub Worker";  Description = "DEV — Background worker";                  Args = ""; Env = "ASPNETCORE_ENVIRONMENT=Development" }
)

function Stop-And-Delete($svcName) {
    $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') {
            Write-Host "  durduruluyor: $svcName"
            Stop-Service -Name $svcName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
        Write-Host "  siliniyor: $svcName"
        & sc.exe delete "$svcName" | Out-Null
        Start-Sleep -Seconds 1
    }
}

function Stop-ForegroundProcesses {
    # Port 61001 kontrol — kullanici dotnet run yapiyorsa uyari
    $webPort = (netstat -ano | Select-String ":61001\s+0\.0\.0\.0:0\s+LISTENING") -match "LISTENING\s+(\d+)"
    if ($Matches -and $Matches[1]) {
        $webPid = $Matches[1]
        $webProc = Get-Process -Id $webPid -ErrorAction SilentlyContinue
        if ($webProc -and $webProc.ProcessName -ne 'CalibraHub.Web') {
            Write-Host "  UYARI: Port 61001'de yabanci process var: PID=$webPid ($($webProc.ProcessName))" -ForegroundColor Yellow
            Write-Host "         Onceden 'dotnet run' yapip biraktiysaniz, manuel durdurun:" -ForegroundColor Yellow
            Write-Host "           Stop-Process -Id $webPid -Force" -ForegroundColor Yellow
            Write-Host "         Aksi halde 'CalibraHub Web' servisi port'a baglanamaz." -ForegroundColor Yellow
        }
    }
}

# ── Uninstall ─────────────────────────────────────────────────────────────
if ($UninstallOnly) {
    Write-Host "[dev-register-services] UNINSTALL modu — servisleri durdurup siliyor..." -ForegroundColor Cyan
    foreach ($svc in $services) { Stop-And-Delete $svc.Name }
    Write-Host "[dev-register-services] Tamamlandi. Dev moduna donus icin:" -ForegroundColor Green
    Write-Host "  dotnet run --project src\CalibraHub.Web" -ForegroundColor Gray
    exit 0
}

# ── Install ───────────────────────────────────────────────────────────────
Write-Host "[dev-register-services] DEV servisleri kuruluyor..." -ForegroundColor Cyan
Write-Host ""

# 1) Foreground process'leri durdur
Write-Host "1) Foreground process'ler temizleniyor..." -ForegroundColor White
Stop-ForegroundProcesses

# 2) Mevcut ayni isimli servisleri sil (idempotent)
Write-Host ""
Write-Host "2) Onceki servisler temizleniyor..." -ForegroundColor White
foreach ($svc in $services) { Stop-And-Delete $svc.Name }

# 3) Servisleri olustur
Write-Host ""
Write-Host "3) Servisler olusturuluyor..." -ForegroundColor White
foreach ($svc in $services) {
    if (-not (Test-Path $svc.Exe)) {
        Write-Host "  ATLANDI: $($svc.Name) — exe yok: $($svc.Exe)" -ForegroundColor Yellow
        continue
    }

    $binPath = if ($svc.Args) { "`"$($svc.Exe)`" $($svc.Args)" } else { "`"$($svc.Exe)`"" }

    Write-Host "  olusturuluyor: $($svc.Name)"
    & sc.exe create "$($svc.Name)" binPath= "$binPath" start= demand DisplayName= "$($svc.Display)" | Out-Null
    & sc.exe description "$($svc.Name)" "$($svc.Description)" | Out-Null
    & sc.exe failure "$($svc.Name)" reset= 86400 actions= restart/5000/restart/5000/restart/10000 | Out-Null

    # Env vars (ASPNETCORE_URLS vs)
    if ($svc.Env) {
        $envValues = $svc.Env -split "`0"
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$($svc.Name)"
        Set-ItemProperty -Path $regPath -Name "Environment" -Type MultiString -Value $envValues
        Write-Host "    env: $($envValues -join ' | ')"
    }
}

# 4) ACL grant
Write-Host ""
Write-Host "4) ACL grant (Authenticated Users → Start/Stop)..." -ForegroundColor White
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $grantScript

# 5) Servisleri baslat
Write-Host ""
Write-Host "5) Servisler baslatiliyor..." -ForegroundColor White
foreach ($svc in $services) {
    $exists = Get-Service -Name $svc.Name -ErrorAction SilentlyContinue
    if ($exists) {
        try {
            Start-Service -Name $svc.Name -ErrorAction Stop
            Write-Host "  OK: $($svc.Name)" -ForegroundColor Green
        } catch {
            Write-Host "  HATA: $($svc.Name) baslamadi: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# 6) Ozet
Write-Host ""
Write-Host "[dev-register-services] Tamamlandi." -ForegroundColor Green
Write-Host ""
Write-Host "Durum:" -ForegroundColor Cyan
Get-Service "CalibraHub Web", "CalibraHub Worker" -ErrorAction SilentlyContinue |
    Format-Table Name, Status, StartType -AutoSize

Write-Host "Sirada: ServiceManager UI'i acin (admin OLMASIN, asInvoker yeterli):" -ForegroundColor Cyan
Write-Host "  src\CalibraHub.ServiceManager\bin\Debug\net10.0-windows\CalibraHub.ServiceManager.exe" -ForegroundColor Gray
Write-Host ""
Write-Host "Geri donmek icin: pwsh installer\dev-register-services.ps1 -UninstallOnly" -ForegroundColor Gray
