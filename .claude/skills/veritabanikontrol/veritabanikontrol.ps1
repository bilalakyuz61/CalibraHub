#Requires -Version 5.1
<#
.SYNOPSIS
    CalibraHub kod <-> DB sema senkron denetimi (release / guncelleme oncesi).

.DESCRIPTION
    1) Bos bir LocalDB'ye yayinlanmis uygulamayi kurar -> initializer TUM semayi olusturur.
    2) Gercek semayi INFORMATION_SCHEMA'dan cikarir (tablo + kolon, ground truth).
    3) src\CalibraHub.Persistence\Repositories\*.cs icindeki [kolon] referanslarini, her repo'nun
       DOKUNDUGU tablolarin gercek kolonlariyla karsilastirir. Semada OLMAYAN referans
       (case-insensitive; underscore gercek karakter, 'is_active' != 'IsActive') = "Invalid column/object"
       riski = release-blocker.
    4) Bilinen false-positive'leri (item_locations gibi repo'nun kendi olusturdugu tablolar, sys.* katalog,
       alias, INFORMATION_SCHEMA meta kolonlari, harici Netsis repo'su) eler.
    5) Test DB'sini + gecici uygulama instance'ini temizler.

    Exit 0 = TEMIZ (yalniz bilinen FP). Exit 1 = YENI uyusmazlik / init hatasi. Exit 2 = on-kosul eksik.

.PARAMETER RepoRoot   Proje koku (varsayilan: skill klasorunun 3 ust dizini).
.PARAMETER AppExe     Yayinlanmis CalibraHub.Web.exe (varsayilan: <RepoRoot>\publish\Web\CalibraHub.Web.exe).
.PARAMETER LocalDbName  Tek-kullanimlik LocalDB adi (denetim sonunda silinir).
.PARAMETER Port       Init sirasinda Kestrel'in dinleyecegi gecici port.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .claude\skills\veritabanikontrol\veritabanikontrol.ps1
#>
param(
    [string]$RepoRoot,
    [string]$AppExe,
    [string]$LocalDbName = 'CalibraSchemaAudit',
    [int]$Port = 61099,
    [int]$InitTimeoutSec = 180
)
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) }
if (-not $AppExe)   { $AppExe   = Join-Path $RepoRoot 'publish\Web\CalibraHub.Web.exe' }
$repoDir = Join-Path $RepoRoot 'src\CalibraHub.Persistence\Repositories'

# Cakisma olmasin diye benzersiz log adi (calistirmalar arasi dosya kilidi sorunu yasanmasin)
$outLog = Join-Path $env:TEMP ("ch_schema_audit_{0}.out.log" -f $PID)
$errLog = Join-Path $env:TEMP ("ch_schema_audit_{0}.err.log" -f $PID)

# --- Bilinen / incelenmis false-positive'ler (dosya::token) ------------------------------------------
# Repo'nun kendi lazy-create ettigi tablolar (item_locations) + sys.foreign_keys katalog kolonlari.
# YENI gercek uyusmazliklar bunlarin DISINDA raporlanir. Yeni bir repo-created tablo / sys.* sorgusu
# eklersen ve dogruladiginsa, buraya ekle ki gurultu yapmasin.
$KnownFalsePositives = @(
    'SqlLogisticsConfigurationRepository.cs::is_active',
    'SqlLogisticsConfigurationRepository.cs::is_default',
    'SqlLogisticsConfigurationRepository.cs::item_id',
    'SqlLogisticsConfigurationRepository.cs::location_id',
    'SqlLogisticsConfigurationRepository.cs::sort_order',
    'SqlLogisticsConfigurationRepository.cs::item_locations',
    'SqlLogisticsConfigurationRepository.cs::location_types',
    'SqlLogisticsConfigurationRepository.cs::stock_unit_conversions',
    'SqlLogisticsConfigurationRepository.cs::object_id',
    'SqlLogisticsConfigurationRepository.cs::parent_object_id'
)
# Harici DB'ye (Netsis vb.) sorgu atan repo'lar — kendi semamiz degil, atla.
$ExcludeRepos = @('SqlIncomingDocumentRepository.cs')

function New-LocalDbConn([string]$db) {
    New-Object System.Data.SqlClient.SqlConnection ("Server=(localdb)\MSSQLLocalDB;Database=$db;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30")
}
function Drop-TestDb {
    try {
        $m = New-LocalDbConn 'master'; $m.Open(); $c = $m.CreateCommand()
        $c.CommandText = "IF DB_ID('$LocalDbName') IS NOT NULL BEGIN ALTER DATABASE [$LocalDbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$LocalDbName]; END"
        [void]$c.ExecuteNonQuery(); $m.Close()
    } catch { Write-Host "  (drop uyari: $($_.Exception.Message))" -ForegroundColor DarkYellow }
}
# Porttaki tum dinleyicileri oldur (orphan temizligi) — HasExited guvenilmez, port bazli oldururuz.
function Stop-Port([int]$p) {
    Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { try { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue } catch {} }
}
$script:proc = $null
function Stop-App {
    Stop-Port $Port
    try { if ($script:proc -and -not $script:proc.HasExited) { Stop-Process -Id $script:proc.Id -Force -ErrorAction SilentlyContinue } } catch {}
}

Write-Host ""
Write-Host "==> CalibraHub  kod <-> DB  sema senkron denetimi" -ForegroundColor Cyan
Write-Host "    repo : $repoDir" -ForegroundColor DarkGray
Write-Host "    exe  : $AppExe" -ForegroundColor DarkGray

if (-not (Test-Path $AppExe)) {
    Write-Host "HATA: Yayinlanmis uygulama bulunamadi." -ForegroundColor Red
    Write-Host "  Once yayinla:" -ForegroundColor Yellow
    Write-Host "    installer\build-installer.ps1" -ForegroundColor Yellow
    Write-Host "    (veya: dotnet publish src\CalibraHub.Web\CalibraHub.Web.csproj -c Release -r win-x64 --self-contained -o publish\Web)" -ForegroundColor Yellow
    exit 2
}
if (-not (Test-Path $repoDir)) { Write-Host "HATA: Repo dizini yok: $repoDir" -ForegroundColor Red; exit 2 }

try {
    # --- 1) Bos DB'ye kur (init semayi olusturur) ---
    Write-Host "  [1/4] Bos LocalDB'ye kurulum: $LocalDbName (port $Port)" -ForegroundColor Gray
    Stop-Port $Port                # onceki calistirmadan orphan varsa temizle
    Start-Sleep -Milliseconds 500
    Drop-TestDb
    foreach ($lf in @($outLog, $errLog)) { if (Test-Path $lf) { Remove-Item $lf -Force -ErrorAction SilentlyContinue } }

    $env:CalibraDatabase__ConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=$LocalDbName;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
    $env:Kestrel__Endpoints__Http__Url     = "http://0.0.0.0:$Port"
    $env:ASPNETCORE_ENVIRONMENT            = 'Development'
    $script:proc = Start-Process -FilePath $AppExe -WorkingDirectory (Split-Path -Parent $AppExe) `
                          -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
                          -NoNewWindow -PassThru

    # Init tespiti: TCP connect ile ($proc.HasExited redirected-console'da guvenilmez).
    # Redirected stdout .NET tarafinda buffer'lanabiliyor (autoflush yok) — "Now listening on"
    # satiri dosyaya dakikalarca gecikmeli yazilabilir, oysa Kestrel bind gercek zamanli bir
    # OS olayidir. Bu yuzden asil "hazir" sinyali port'a gercek TCP connect denemesi; log sadece
    # INIT-ERROR (Invalid column/object vb.) icin hala taranir (o satirlar erken/kucuk hacimde
    # yazildigindan pratikte flush gecikmesi INIT-ERROR tespitini nadiren geciktirir).
    $rxErr = 'Invalid column name|Invalid object name|Unhandled exception|Hosting failed'
    $deadline = (Get-Date).AddSeconds($InitTimeoutSec)
    $initState = 'TIMEOUT'
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ($script:proc.HasExited) { $initState = 'EXITED'; break }
        $txt = ''
        try { $txt = [System.IO.File]::ReadAllText($outLog) } catch {}
        if ($txt -match $rxErr) { $initState = 'INIT-ERROR'; break }
        $tcp = New-Object System.Net.Sockets.TcpClient
        try {
            $iar = $tcp.BeginConnect('127.0.0.1', $Port, $null, $null)
            if ($iar.AsyncWaitHandle.WaitOne(300) -and $tcp.Connected) { $initState = 'OK'; break }
        } catch {} finally { $tcp.Close() }
    }

    if ($initState -ne 'OK') {
        Write-Host "  HATA: Init temiz tamamlanmadi (durum=$initState)." -ForegroundColor Red
        Write-Host "  --- stdout (son 15) ---" -ForegroundColor DarkGray
        if (Test-Path $outLog) { Get-Content $outLog -Tail 15 | ForEach-Object { Write-Host "    $_" } }
        if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) {
            Write-Host "  --- stderr (son 8) ---" -ForegroundColor DarkGray
            Get-Content $errLog -Tail 8 | ForEach-Object { Write-Host "    $_" }
        }
        exit 1
    }

    # --- 2) Gercek semayi cikar ---
    Write-Host "  [2/4] Gercek sema cikariliyor (INFORMATION_SCHEMA)" -ForegroundColor Gray
    $tableCols = @{}; $tableSet = New-Object System.Collections.Generic.HashSet[string]
    $conn = New-LocalDbConn $LocalDbName; $conn.Open(); $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo'"
    $r = $cmd.ExecuteReader()
    while ($r.Read()) {
        $t = $r.GetString(0).ToLowerInvariant(); $c = $r.GetString(1).ToLowerInvariant()
        if (-not $tableCols.ContainsKey($t)) { $tableCols[$t] = New-Object System.Collections.Generic.HashSet[string] }
        [void]$tableCols[$t].Add($c); [void]$tableSet.Add($t)
    }
    $r.Close(); $conn.Close()
    Stop-App   # sema alindi, gecici uygulamayi (port + proc) kapat
    $colCount = 0; foreach ($t in $tableCols.Keys) { $colCount += $tableCols[$t].Count }
    Write-Host "        $($tableSet.Count) tablo / $colCount kolon" -ForegroundColor DarkGray

    # --- 3) Diff: repo [kolon] referanslari vs dokundugu tablolarin gercek kolonlari ---
    Write-Host "  [3/4] Repo SQL kolonlari semayla karsilastiriliyor" -ForegroundColor Gray
    $metaCols = @('column_name','data_type','is_nullable','ordinal_position','table_name','table_schema')
    $rxTok   = [regex]'\[([A-Za-z_][A-Za-z0-9_]*)\]'
    $rxAliasCol = [regex]'(?i)\bAS\s*\[([A-Za-z_][A-Za-z0-9_]*)\]'
    $flagged = New-Object System.Collections.Generic.List[string]
    foreach ($f in (Get-ChildItem $repoDir -Filter '*.cs')) {
        if ($ExcludeRepos -contains $f.Name) { continue }
        $text = Get-Content $f.FullName -Raw
        # Bu repo'nun dokundugu (semada var olan) tablolar
        $refTables = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in $rxTok.Matches($text)) { $l = $m.Groups[1].Value.ToLowerInvariant(); if ($tableSet.Contains($l)) { [void]$refTables.Add($l) } }
        if ($refTables.Count -eq 0) { continue }   # literal tablo yok (interpolated/harici) -> spot-dogrula
        $union = New-Object System.Collections.Generic.HashSet[string]
        foreach ($t in $refTables) { foreach ($c in $tableCols[$t]) { [void]$union.Add($c) } }
        $aliases = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in $rxAliasCol.Matches($text)) { [void]$aliases.Add($m.Groups[1].Value.ToLowerInvariant()) }
        $seen = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in $rxTok.Matches($text)) {
            $tok = $m.Groups[1].Value; $l = $tok.ToLowerInvariant()
            if (-not $seen.Add($l)) { continue }
            if ($tableSet.Contains($l) -or $union.Contains($l) -or $aliases.Contains($l)) { continue }
            # yalniz snake_case gorunumlu (underscore'lu, lowercase) gercek kolon adaylari
            if ($l -notmatch '^[a-z][a-z0-9_]*_[a-z0-9]+$') { continue }
            if ($l -match '^(ix_|ux_|pk_|fk_|df_)') { continue }
            if ($metaCols -contains $l) { continue }
            $flagged.Add($f.Name + '::' + $tok)
        }
    }

    # --- 4) Rapor ---
    Drop-TestDb
    $new = @($flagged | Where-Object { $KnownFalsePositives -notcontains $_ } | Sort-Object -Unique)
    Write-Host "  [4/4] Sonuc" -ForegroundColor Gray
    Write-Host ""
    if ($new.Count -eq 0) {
        Write-Host "  TEMIZ  ->  Kod ile DB semasi senkron. Sifirdan kurulum kolon/tablo-adi hatasi vermez." -ForegroundColor Green
        Write-Host "  (Bilinen $($KnownFalsePositives.Count) false-positive elendi: item_locations + sys.* katalog.)" -ForegroundColor DarkGray
        exit 0
    } else {
        Write-Host "  UYUSMAZLIK  ->  $($new.Count) YENI referans fresh semada YOK (release-blocker):" -ForegroundColor Red
        foreach ($x in $new) { Write-Host ("     " + ($x -replace '::', '  ::  ')) -ForegroundColor Yellow }
        Write-Host ""
        Write-Host "  Duzelt (hedef PascalCase):" -ForegroundColor Gray
        Write-Host "   - repo snake + DB Pascal  -> repo SQL'ini Pascal yap (cerrahi; ayni tabloda bilincli snake kalabilir)" -ForegroundColor Gray
        Write-Host "   - repo Pascal + DB snake  -> o tablonun Ensure* metoduna idempotent sp_rename ekle" -ForegroundColor Gray
        Write-Host "   - gercek false-positive (alias/interpolated/repo-created/sys) -> script'te KnownFalsePositives'e ekle" -ForegroundColor Gray
        Write-Host "  Duzelttikten sonra bu denetimi TEKRAR calistir (TEMIZ cikana dek)." -ForegroundColor Gray
        exit 1
    }
}
finally {
    Stop-App
    Start-Sleep -Milliseconds 400
    foreach ($lf in @($outLog, $errLog)) { if (Test-Path $lf) { Remove-Item $lf -Force -ErrorAction SilentlyContinue } }
}
