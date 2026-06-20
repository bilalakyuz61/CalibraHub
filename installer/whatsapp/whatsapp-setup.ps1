#requires -RunAsAdministrator
<#
.SYNOPSIS
  CalibraHub WhatsApp Bridge (Node.js sidecar) kurulum + Windows servis hazirligi.

.DESCRIPTION
  Inno Setup'tan postInstall asamasinda Grafana setup'tan sonra cagrilir.
  Adimlar:
    1) Node.js (>=18) yuklu mu kontrol et — yoksa skip + uyari logla
    2) Bridge klasorunde "npm install --production" calistir (Chromium/puppeteer dahil ~80MB)
    3) "node install-service.js" cagirip "CalibraHubWhatsAppBridge" Windows servisi olustur
    4) appsettings.json'daki WhatsApp:WebQrBridgeUrl alanina secilen porta gore set et

  Tum adimlar idempotent: tekrar calistirilabilir, mevcut node_modules korunur.
  Hata olursa exit 0 ile sessizce devam eder — Cloud API kullananlar icin
  Bridge servisi opsiyonel.

.PARAMETER InstallRoot
  CalibraHub kok dizini, ornek: "C:\Program Files\CalibraHub"

.PARAMETER WebAppSettingsPath
  Web servisinin appsettings.json yolu — WhatsApp:WebQrBridgeUrl guncellemesi icin.

.PARAMETER BridgePort
  Bridge'in dinleyecegi port (default: 61100). Inno Setup wizard'inda kullanici
  girer; kontrol edilmis olarak buraya gelir.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [Parameter(Mandatory = $true)]
    [string]$WebAppSettingsPath,

    [int]$BridgePort = 61100
)

$ErrorActionPreference = "Continue"

$bridgeHome  = Join-Path $InstallRoot "WhatsAppBridge"
$serviceName = "CalibraHubWhatsAppBridge"
$logFile     = Join-Path $InstallRoot "Logs\whatsapp-setup.log"

# Log dizini garanti
$logDir = Split-Path $logFile -Parent
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$ts] [$Level] $Message"
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

Write-Log "WhatsApp Bridge kurulum baslatildi. InstallRoot=$InstallRoot, Port=$BridgePort"

# ── Bridge klasoru var mi? ──────────────────────────────────────────────
if (-not (Test-Path $bridgeHome)) {
    Write-Log "Bridge klasoru bulunamadi: $bridgeHome — kurulum atlandi." "WARN"
    exit 0
}

# ── Node.js kontrolu ─────────────────────────────────────────────────────
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    Write-Log "Node.js bulunamadi. WhatsApp Bridge servisi kurulamadi. Lutfen Node.js >=18 kurun ve setup'i tekrar calistirin." "WARN"
    exit 0
}

try {
    $nodeVersion = & node --version
    Write-Log "Node.js bulundu: $nodeVersion"
    # v18.x.x format kontrolu — major version >= 18
    if ($nodeVersion -match '^v(\d+)\.') {
        $major = [int]$Matches[1]
        if ($major -lt 18) {
            Write-Log "Node.js $nodeVersion ancak v18+ gerekli. WhatsApp Bridge atlandi." "WARN"
            exit 0
        }
    }
} catch {
    Write-Log "Node.js versiyon okunamadi: $($_.Exception.Message)" "WARN"
    exit 0
}

# ── npm install (Chromium dahil, ~80MB ilk seferde) ─────────────────────
Push-Location $bridgeHome
try {
    if (Test-Path (Join-Path $bridgeHome "node_modules")) {
        Write-Log "node_modules mevcut — npm install atlandi (idempotent)."
    } else {
        Write-Log "npm install --production calisiyor... (~80MB Chromium indirecek)"
        $npmOutput = & npm install --production 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log "npm install basarisiz (exit $LASTEXITCODE). Internet baglantisini kontrol edin." "WARN"
            $npmOutput | ForEach-Object { Write-Log $_ "WARN" }
            exit 0
        }
        Write-Log "npm install tamamlandi."
    }
} catch {
    Write-Log "npm install hatasi: $($_.Exception.Message)" "WARN"
    Pop-Location
    exit 0
} finally {
    Pop-Location
}

# ── Mevcut servisi durdur + sil (idempotent) ────────────────────────────
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Log "Mevcut servis bulundu — durduruluyor ve kaldiriliyor."
    try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
    Push-Location $bridgeHome
    try {
        & node uninstall-service.js 2>&1 | Out-Null
    } catch {
        Write-Log "uninstall-service.js calisirken uyari: $($_.Exception.Message)" "WARN"
    } finally {
        Pop-Location
    }
    Start-Sleep -Seconds 2
}

# ── Servis olustur (install-service.js node-windows ile) ────────────────
Push-Location $bridgeHome
try {
    Write-Log "WhatsApp Bridge servisi olusturuluyor..."
    # PORT env variable ile port'u install-service.js icine geciriyoruz —
    # index.js zaten process.env.PORT'u okuyor (default 61100).
    $env:PORT = "$BridgePort"
    $installOutput = & node install-service.js 2>&1
    $installOutput | ForEach-Object { Write-Log $_ }
    if ($LASTEXITCODE -ne 0) {
        Write-Log "install-service.js basarisiz (exit $LASTEXITCODE)." "WARN"
        Pop-Location
        exit 0
    }
} finally {
    Pop-Location
}

# ── appsettings.json'daki WhatsApp:WebQrBridgeUrl guncelle ──────────────
if (Test-Path $WebAppSettingsPath) {
    try {
        $jsonRaw = Get-Content -Path $WebAppSettingsPath -Raw -Encoding UTF8
        # Basit regex-replace — appsettings.json'da WhatsApp:WebQrBridgeUrl varsa
        # sadece port'u guncelliyoruz. Yoksa el surmuyoruz (DB'de tutulan ayar
        # varsa o oncelikli olur).
        $newUrl = "http://127.0.0.1:$BridgePort"
        if ($jsonRaw -match '"WebQrBridgeUrl"\s*:\s*"[^"]*"') {
            $jsonRaw = $jsonRaw -replace '("WebQrBridgeUrl"\s*:\s*")[^"]*(")', "`$1$newUrl`$2"
            Set-Content -Path $WebAppSettingsPath -Value $jsonRaw -Encoding UTF8 -NoNewline
            Write-Log "appsettings.json -> WhatsApp.WebQrBridgeUrl=$newUrl olarak guncellendi."
        } else {
            Write-Log "appsettings.json icinde WhatsApp.WebQrBridgeUrl alani yok — atlandi (DB'deki WhatsAppConfig kullanilacak)."
        }
    } catch {
        Write-Log "appsettings.json guncellenirken hata: $($_.Exception.Message)" "WARN"
    }
}

Write-Log "WhatsApp Bridge kurulumu tamamlandi."
exit 0
