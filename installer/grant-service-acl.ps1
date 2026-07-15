<#
.SYNOPSIS
  CalibraHub.* Windows servislerine Authenticated Users (AU) icin SERVICE_START
  ve SERVICE_STOP yetkisi grant eder. Bu sayede CalibraHub Servis Yoneticisi
  (CalibraHub.ServiceManager.exe) standart kullanici hakki ile (UAC SORMADAN)
  servisleri baslatip durdurabilir.

.DESCRIPTION
  Inno Setup'tan postInstall asamasinda cagrilir (CalibraHub.iss [Run] section).
  Idempotent — defalarca calistirilabilir, mevcut ACL'leri ezmez, sadece AU
  ACE'sini ekler.

  Mantik:
    1) Her servis icin sc.exe sdshow ile mevcut SDDL'i al
    2) AU (S-1-5-11) icin LCRPWP (LOCAL_CONTROL + SERVICE_START + SERVICE_STOP)
       ACE'si zaten var mi kontrol et
    3) Yoksa append edip sc.exe sdset ile yeniden yaz

  ACE detayi:
    L = SERVICE_INTERROGATE
    C = SERVICE_PAUSE_CONTINUE
    R = SERVICE_QUERY_STATUS
    P = SERVICE_QUERY_CONFIG (read service config)
    Wait — actual SDDL letters for service rights:
       CC = SERVICE_QUERY_CONFIG
       LC = SERVICE_QUERY_STATUS
       SW = SERVICE_ENUMERATE_DEPENDENTS
       RP = SERVICE_START
       WP = SERVICE_STOP
       DT = SERVICE_PAUSE_CONTINUE
       LO = SERVICE_INTERROGATE
       CR = SERVICE_USER_DEFINED_CONTROL

  AU = Authenticated Users SID (S-1-5-11). Anon/Guest haric herkes.

  Bu script ADMIN olarak calistirilmali (sc sdset elevation gerektirir).

.PARAMETER ServiceNames
  Yetki verilecek servis adlari. Default: CalibraHub Web/Worker.

.EXAMPLE
  pwsh installer\grant-service-acl.ps1
  # Tum CalibraHub servislerine AU yetkisi grant eder

.EXAMPLE
  pwsh installer\grant-service-acl.ps1 -ServiceNames "CalibraHub Web"
  # Sadece tek servis
#>
param(
    [string[]]$ServiceNames = @("CalibraHub Web", "CalibraHub Worker")
)

$ErrorActionPreference = "Stop"

# ── Admin kontrol ─────────────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[grant-service-acl] HATA: Bu script ADMIN PowerShell'de calistirilmali." -ForegroundColor Red
    Write-Host "                    Servis SDDL'lerini sadece admin set edebilir." -ForegroundColor Red
    exit 1
}

# AU (Authenticated Users) icin Allow ACE — start/stop/query yetkilerini ekler
# CCLCSWRPWPDTLOCRRC = QueryConfig + QueryStatus + EnumDependents +
#                      Start + Stop + PauseContinue + Interrogate + UserDefinedControl + ReadControl
$auAce = "(A;;CCLCSWRPWPDTLOCRRC;;;AU)"

foreach ($svc in $ServiceNames) {
    Write-Host "[grant-service-acl] $svc" -NoNewline

    # Servis var mi?
    $exists = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if (-not $exists) {
        Write-Host " — yuklu degil, atlandi." -ForegroundColor Yellow
        continue
    }

    # Mevcut SDDL'i al
    $current = (& sc.exe sdshow "$svc") -join ""
    if (-not $current -or $current -match "FAILED") {
        Write-Host " — sdshow basarisiz, atlandi." -ForegroundColor Yellow
        continue
    }

    # AU icin LC|RP|WP iceren ACE zaten var mi? (L=QueryStatus, R=Start, W=Stop hosts the right letters)
    # Daha basit: ;AU) iceren ACE varsa idempotent kabul et.
    if ($current -match "\(A;[^)]*;[^)]*;;;AU\)") {
        Write-Host " — AU yetkisi zaten var, atlandi." -ForegroundColor Green
        continue
    }

    # SDDL formati: O:<owner>G:<group>D:<dacl>S:<sacl>
    # AU ACE'sini DACL sonuna ekle. D:(...)(...)... -> D:(...)(...)...new_ace
    if ($current -notmatch "D:") {
        Write-Host " — gecersiz SDDL formati, atlandi." -ForegroundColor Yellow
        continue
    }

    # SACL varsa onun oncesine ekle, yoksa SDDL sonuna ekle.
    if ($current -match "S:") {
        $newSddl = $current -replace "(S:)", "$auAce`$1"
    } else {
        $newSddl = $current + $auAce
    }

    & sc.exe sdset "$svc" "$newSddl" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host " — AU yetkisi eklendi." -ForegroundColor Green
    } else {
        Write-Host " — sdset basarisiz (exit=$LASTEXITCODE)." -ForegroundColor Red
    }
}

Write-Host "[grant-service-acl] Tamamlandi."
