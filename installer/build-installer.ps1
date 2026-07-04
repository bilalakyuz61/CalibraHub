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
    [string]$Version = "2.1.17",
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
    # WhatsAppBridge silinmez — node.js sidecar, kaynak kodu repo digi (git'ten restore edilir,
    # node_modules ilk seferlik npm install ile yaratilir). Build her seferinde Bridge'i
    # yeniden indirsek kullanici .wwebjs_auth/ (WhatsApp pairing credentials) kaybeder.
    $subFolders = @('Web', 'Worker', 'Designer', 'ServiceManager')
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

# --- CalibraHub.Web.csproj versiyon guncelle ---
# Uygulama icindeki typeof(Program).Assembly.GetName().Version installer versiyonu ile eslessin.
Write-Step ("CalibraHub.Web.csproj versiyonu guncelleniyor ($Version)...")
$WebCsproj = Join-Path $RootDir "src\CalibraHub.Web\CalibraHub.Web.csproj"
$csprojContent = Get-Content $WebCsproj -Raw
$csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
[System.IO.File]::WriteAllText($WebCsproj, $csprojContent, [System.Text.Encoding]::UTF8)
Write-Host ("  Guncellendi: " + $WebCsproj) -ForegroundColor Gray

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

# --- WhatsApp Bridge (Node.js sidecar) - temiz payload stage ---
# publish\WhatsAppBridge ayni zamanda CALISAN node-windows servis dizini olabilir; wrapper
# daemon\*.err.log + yuklu node_modules native modullerini kilitli tutar → Inno compress
# kilit hatasi verir (exit 2). Bu yuzden payload'i AYRI temiz bir stage dizinine
# (WhatsAppBridge_pkg) robocopy ile kopyalar, runtime artefaktlarini HARIC tutariz:
#   node_modules → kurulum sonrasi npm install ile yeniden uretilir (bkz. .iss [Run])
#   daemon, .wwebjs_cache, .wwebjs_auth, session-data, *.log → salt runtime, pakete girmez
# robocopy haric tutulan/kilitli dosyalari hic acmaz → kilit sorunu ortadan kalkar.
Write-Step "WhatsApp Bridge payload stage'leniyor (runtime artefaktlari haric)..."
$BridgeSrc = Join-Path $RootDir "tools\CalibraHubWhatsAppBridge"
if (-not (Test-Path $BridgeSrc)) { $BridgeSrc = Join-Path $PublishDir "WhatsAppBridge" }  # fallback: canli servis dizini
$BridgeDst = Join-Path $PublishDir "WhatsAppBridge_pkg"
if (Test-Path $BridgeDst) { Remove-Item $BridgeDst -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $BridgeSrc) {
    New-Item -ItemType Directory -Path $BridgeDst -Force | Out-Null
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # robocopy native stderr NativeCommandError'a sarilmasin
    & robocopy $BridgeSrc $BridgeDst /E /XD node_modules daemon .wwebjs_cache .wwebjs_auth session-data .git /XF *.log /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    $rc = $LASTEXITCODE
    $ErrorActionPreference = $prevEAP
    cmd /c "exit 0"   # robocopy exit 0-7 = basari; LASTEXITCODE'u sonraki adimlar icin sifirla
    if ($rc -ge 8) {
        Write-Host ("HATA: robocopy Bridge stage basarisiz (exit " + $rc + ")") -ForegroundColor Red
        exit 1
    }
    $cnt = (Get-ChildItem $BridgeDst -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    Write-Host ("  Bridge payload stage'lendi: " + $BridgeDst + " (" + $cnt + " dosya)") -ForegroundColor Gray
} else {
    Write-Host ("UYARI: Bridge kaynagi bulunamadi (tools + publish\WhatsAppBridge yok) - atlandi.") -ForegroundColor Yellow
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
