#Requires -Version 5.1
<#
.SYNOPSIS
    CalibraHub installer build script.
    Projeyi publish eder ve Inno Setup ile setup.exe olusturur.

.PARAMETER Version
    Uygulama versiyonu (varsayilan: 1.0.0)

.PARAMETER Configuration
    Build konfigurasyonu (varsayilan: Release)

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version "2.0.0"
#>
param(
    [string]$Version = "2.1.15",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir    = Split-Path -Parent $ScriptDir
# Publish ciktilari (ara payload — Web/Worker/ServiceManager exe + dll'leri) repo
# icindeki publish/ klasorune yazilir; .gitignore ile dis tutulur.
$PublishDir = Join-Path $RootDir "publish"
# Setup .exe ciktisi (final installer) merkezi setup klasorune yazilir — boylece
# tum projelerin installer'lari D:\Projeler\Setup\<ProjectName>\ altinda toplanir.
# CalibraHub.iss icinde OutputDir=D:\Projeler\Setup\CalibraHub ile eslestirildi.
$OutputDir  = "D:\Projeler\Setup\CalibraHub"
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

# --- Yardimci fonksiyonlar ---
function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host ("==> " + $msg) -ForegroundColor Cyan
}

function Assert-Success([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("HATA: " + $step + " adimi basarisiz oldu (exit " + $LASTEXITCODE + ")") -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# --- Inno Setup bul ---
$InnoExe = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $InnoExe) {
    Write-Host "HATA: Inno Setup 6 bulunamadi." -ForegroundColor Red
    Write-Host "Lutfen https://jrsoftware.org/isdl.php adresinden indirip kurun." -ForegroundColor Red
    exit 1
}

# --- Temizle ---
# Sadece proje publish subfolders'ini temizle, eski Setup .exe dosyalarini KORU.
# Boylece kullanici onceki versiyonlari da elinde tutabilir (rollback / arsiv).
Write-Step "Onceki publish subfolder'leri temizleniyor (eski Setup .exe'leri korunuyor)..."
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir | Out-Null
} else {
    $subFolders = @('Web', 'Worker', 'Designer', 'ServiceManager', 'WhatsAppBridge')
    foreach ($sub in $subFolders) {
        $path = Join-Path $PublishDir $sub
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
            Write-Host ("  Silindi: " + $path) -ForegroundColor Gray
        }
    }
    # Eski setup exe'leri listele (silinmedi)
    $oldSetups = Get-ChildItem -Path $PublishDir -Filter "CalibraHub-Setup-*.exe" -File -ErrorAction SilentlyContinue
    if ($oldSetups) {
        Write-Host ("  Korunan setup'lar (" + $oldSetups.Count + ")") -ForegroundColor DarkGray
        foreach ($f in $oldSetups) {
            $sizeMB = [math]::Round($f.Length / 1048576, 1)
            Write-Host ("    - " + $f.Name + " (" + $sizeMB + " MB)") -ForegroundColor DarkGray
        }
    }
}

# --- React bundle (Vite) - publish oncesi zorunlu, wwwroot/react/* publish'e dahil edilir ---
# PowerShell $ErrorActionPreference=Stop iken native exe stderr'i NativeCommandError'a
# sarilir ve script durur (Vite uretiyor: signalr Utils.js icin __PURE__ comment warning).
# cmd /c "... 2>&1" ile stderr'i stdout'a cmd seviyesinde merge ederiz, PowerShell tek
# stream gorur. ErrorActionPreference de gecici olarak Continue yapilir.
Write-Step "React bundle derleniyor (npm run build)..."
$ClientAppDir = Join-Path $RootDir "src\CalibraHub.Web\ClientApp"
if (Test-Path (Join-Path $ClientAppDir "package.json")) {
    Push-Location $ClientAppDir
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if (-not (Test-Path "node_modules")) {
            Write-Host "  node_modules yok - npm install calistiriliyor..." -ForegroundColor Gray
            cmd.exe /c "npm install --silent 2>&1"
            Assert-Success "npm install"
        }
        cmd.exe /c "npm run build 2>&1"
        Assert-Success "npm run build"
    } finally {
        $ErrorActionPreference = $prevEAP
        Pop-Location
    }
} else {
    Write-Host ("UYARI: ClientApp/package.json bulunamadi: " + $ClientAppDir + " - atlandi.") -ForegroundColor Yellow
}

# --- Publish: Web ---
Write-Step "CalibraHub.Web publish ediliyor (win-x64, self-contained)..."
& "$env:ProgramFiles\dotnet\dotnet.exe" publish "$RootDir\src\CalibraHub.Web\CalibraHub.Web.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    --output "$PublishDir\Web" `
    --nologo
Assert-Success "dotnet publish Web"

# --- Publish: Worker ---
Write-Step "CalibraHub.Worker publish ediliyor (win-x64, self-contained)..."
& "$env:ProgramFiles\dotnet\dotnet.exe" publish "$RootDir\src\CalibraHub.Worker\CalibraHub.Worker.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    --output "$PublishDir\Worker" `
    --nologo
Assert-Success "dotnet publish Worker"

# --- Publish: Designer (WinForms tray app + FastReport koprusu) ---
# 2026-05-20: Designer projesi kaldirildi (FastReport bagimliligi sonlandirildi).
# Csproj yoksa adim atlanir; Inno script Designer source'unu skipifsourcedoesntexist
# bayrağıyla aldigi icin installer compile devam eder, sadece kisayollar olmaz.
$DesignerCsproj = Join-Path $RootDir "src\CalibraHub.Designer\CalibraHub.Designer.csproj"
if (Test-Path $DesignerCsproj) {
    Write-Step "CalibraHub.Designer publish ediliyor (win-x64, self-contained, WinForms)..."
    & "$env:ProgramFiles\dotnet\dotnet.exe" publish $DesignerCsproj `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        --output "$PublishDir\Designer" `
        --nologo
    Assert-Success "dotnet publish Designer"
} else {
    Write-Step "CalibraHub.Designer atlandi (proje silinmis)."
}

# --- 2026-06-19: ServiceManager KALDIRILDI ---
# Bagimsiz WinForms tray app projeden cikarildi. Web/Worker servisleri Windows'un
# services.msc snap-in'i ile yonetilir; ileride benzer bir UI gerekirse Web icine
# /Admin/Services sayfasi olarak entegre edilir.

# --- WhatsApp Bridge (Node.js sidecar) - Source kopyala ---
Write-Step "CalibraHubWhatsAppBridge kaynaklari publish/WhatsAppBridge'e kopyalaniyor..."
$BridgeSrc = Join-Path $RootDir "tools\CalibraHubWhatsAppBridge"
$BridgeDst = Join-Path $PublishDir "WhatsAppBridge"
if (Test-Path $BridgeSrc) {
    New-Item -ItemType Directory -Path $BridgeDst -Force | Out-Null
    $exclude = @('node_modules', 'session-data', '.git', '*.log')
    Get-ChildItem -Path $BridgeSrc -Force | Where-Object {
        $name = $_.Name
        $skip = $false
        foreach ($pattern in $exclude) {
            if ($name -like $pattern) { $skip = $true; break }
        }
        -not $skip
    } | Copy-Item -Destination $BridgeDst -Recurse -Force
    Write-Host ("  Bridge dosyalari kopyalandi: " + $BridgeDst) -ForegroundColor Gray
} else {
    Write-Host ("UYARI: Bridge kaynak klasoru bulunamadi: " + $BridgeSrc + " - atlandi.") -ForegroundColor Yellow
}

# --- Grafana KALDIRILDI (2026-06-19) ---
# Yeni Rapor Tasarimcisi + Pano arayuzu Grafana'nin yerini aldi.
# Grafana zip cache + bundling artik yok; installer ~70 MB kuculdu.
# Inno script (.iss) Grafana dosyalarini "skipifsourcedoesntexist" ile bekliyor;
# zip yoksa Files section sessizce atlar; Run section'daki grafana-setup.ps1
# cagrisi .iss icinde kosullu (FileExists) hale getirildi.

# --- .NET 10 ASP.NET Core Hosting Bundle cache - installer'a bundle edilir ---
# winget / aka.ms bagimliligini kaldirir; ilk kurulumda dahi internet gerekmez.
# aka.ms/dotnet/10.0/dotnet-hosting-win.exe surekli en son patch'e cozumlenir;
# build sirasinda redirect takip edilip gercek dosya cache'lenir.
$DotNetExePath = Join-Path "$ScriptDir\dependencies" "dotnet-hosting-10-win.exe"
$DotNetExeUrl  = "https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe"

Write-Step ".NET 10 ASP.NET Core Hosting Bundle cache kontrolu..."
if (-not (Test-Path $DotNetExePath)) {
    Write-Host ("  Indiriliyor (ilk kez): " + $DotNetExeUrl) -ForegroundColor Gray
    try {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $DotNetExeUrl -OutFile $DotNetExePath -UseBasicParsing
        $sizeMB = [math]::Round((Get-Item $DotNetExePath).Length / 1048576, 1)
        Write-Host ("  Indirildi: " + $DotNetExePath + " (" + $sizeMB + " MB)") -ForegroundColor Gray
    } catch {
        Write-Host ("  UYARI: .NET 10 Hosting Bundle indirilemedi: " + $_.Exception.Message) -ForegroundColor Yellow
        Write-Host "  Installer .NET 10 bundle'siz derlenecek; ilk kurulumda internet'ten indirilecek." -ForegroundColor Yellow
    }
} else {
    $sizeMB = [math]::Round((Get-Item $DotNetExePath).Length / 1048576, 1)
    Write-Host ("  Cache: " + $DotNetExePath + " (" + $sizeMB + " MB) - tekrar indirilmeyecek.") -ForegroundColor Gray
}

# --- Inno Setup derle ---
Write-Step ("Installer derleniyor (v" + $Version + ")...")
& $InnoExe ("/DAppVersion=" + $Version) "$ScriptDir\CalibraHub.iss"
Assert-Success "Inno Setup"

$SetupFile = Join-Path $OutputDir ("CalibraHub-Setup-" + $Version + ".exe")
if (Test-Path $SetupFile) {
    $bytes = (Get-Item $SetupFile).Length
    $sizeMB = [math]::Round($bytes / 1048576, 1)
    Write-Host ""
    Write-Host "Basarili! Installer olusturuldu:" -ForegroundColor Green
    Write-Host ("  " + $SetupFile + " (" + $sizeMB + " MB)") -ForegroundColor Green
} else {
    Write-Host ("UYARI: Beklenen dosya bulunamadi: " + $SetupFile) -ForegroundColor Yellow
}
