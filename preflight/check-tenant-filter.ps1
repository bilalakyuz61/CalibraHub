# CalibraHub — Kiraci (sirket) suzgeci denetcisi
#
# NEDEN: 2026-08-27'de kiraci ayrimi "her sorguya WHERE CompanyId = @CompanyId" yontemiyle
# yapilmaya karar verildi (RLS yerine; RLS korudugu tabloda sp_rename'i ENGELLIYOR, test
# edildi). Bu yontemin tek zayifligi INSAN DIKKATI: bir WHERE'i unutmak, o sorgunun TUM
# sirketlerin verisini dondurmesi demek — ve hicbir hata vermez, sessizce yanlis calisir.
# Bu tarayici o unutmayi mekanik olarak yakalar.
#
# TASARIM (preflight/README.md ile ayni): sifir false-negative hedefi, false-positive'e
# tolerans. Cikti bir "gozden gecir" listesidir — her satir bir bug DEGIL.
#
# IKI KOR NOKTA GIDERILDI (ilk surumlerde vardi, ikisi de bulguyu KUCUK gosteriyordu):
#   1. CompanyId'yi ifadenin her yerinde aramak — SELECT kolon listesinde gecmesi de
#      "suzgec var" sayiliyordu. Artik YALNIZ WHERE'den sonrasina bakilir.
#   2. Tablo adinin degiskenle yazilmasi (FROM {_docTable}) — bu projede sorgularin 806'si
#      boyle, duz yazilan yalniz 59. Artik degiskenler cozulur.
#
# KAPSAM SINIRI (durustce): statik tarama, calisma zamaninda kurulan sorgulari, rapor
# motorunun calistirdigi kullanici SQL'ini ve /ViewBuilder'in gelismis SQL kacis kapisini
# GOREMEZ. O yollar icin RLS uykuda hazir (CalibraDatabaseInitializer.RlsPilotTables).
#
# Kullanim:  powershell -File preflight\check-tenant-filter.ps1 [-Table Document] [-Detail]
[CmdletBinding()]
param(
    [string]$Table,
    [switch]$Detail,
    # Yalniz YAZMA yollarini goster (UPDATE/DELETE/INSERT). Oncelik burasidir:
    # okuma sizintisi can sikicidir, yazma sizintisi GERI DONUSSUZDUR — baska
    # sirketin kaydi ezilir ya da silinir.
    [switch]$MutationsOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$srcDirs = @('src\CalibraHub.Persistence', 'src\CalibraHub.Web', 'src\CalibraHub.Application') |
    ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }

# GLOBAL tablolar — kiraci ayrimi disinda. CalibraDatabaseInitializer.CompanyScopeExemptTables
# ile elle senkron tutulur.
$globalTables = @(
    'Company','__SchemaVersion','Forms','PermissionDef','PermissionGroup','Field','FieldGroup',
    'Country','City','District','Neighborhood','Village','PostalLocality',
    'GlobalLock','LicenseConfig','GateCredential','AiProvider','IntegrationProvider',
    'UiLabelTranslation','PageComment','PageCommentActivity','PageCommentImage',
    'PageCommentRevision','PLT_SISTEM_LOG','whatsapp_safety_rules',
    # MASTER DB'de duran, bilincli olarak sirketler arasi ortak tablolar. Kodda belgeli:
    # "cross-company ... per-company DB mimarisine GIRMEZ (CompanyId YOK)". Toplu migration
    # ilk surumde bunlara da kolon eklemisti; kolonlar dusuruldu, muafiyet burada da kayitli.
    'Attachment','DocumentCategory',
    # Kimlik/parola akisi sirketten BAGIMSIZ: ForgotPassword/ResetPassword anonimdir ve
    # e-posta ile TUM sirketlerdeki kullaniciyi arar. Suzgec koymak ilk sirket disindaki
    # herkesin parola sifirlamasini sessizce kirardi.
    'Users',
    # Dis sistemin (e-fatura entegratoru) ALL_CAPS semasi — CalibraHub olusturmuyor, bu
    # sunucuda hicbir veritabaninda YOKLAR. PLT_SISTEM_LOG ile ayni sinif.
    'CBT_EBELGEMAS','CBT_EBELGEMASTAX','CBT_EBELGEKALEM','CBT_EBELGEKALEMTAX'
)

$findings = New-Object System.Collections.Generic.List[object]
$scanned = 0

# Degisken -> tablo adi:  _docTable = $"[{s}].[Document]";
$varDefRx = [regex]'(?m)(_\w+)\s*=\s*\$?"[^"]*\[(\w+)\]\s*"'
# Tabloya dokunan ifade basi

# 2026-08-28 - DUZ GORUNUM (v_Flat_*) KOR NOKTASI:
# Bu gorunumler "SELECT base.*" ile uretilir ve HIC WHERE tasimaz, yani tum sirketlerin
# satirlarini gosterirler. Onlari okuyan kod ise adlarini calisma zamaninda kurar:
#     var viewName = "v_Flat_" + formCode;   ->   FROM [{_schema}].[{viewName}]
# Eski desen KOSELI PARANTEZ ICINDEKI interpolasyonu ( [{viewName}] ) hic tanimiyordu; bu
# yuzden FormMetadataService ve SqlFormLinesRepository icindeki UC suzgecsiz okuma taramada
# GORUNMUYORDU (elle bulundu). Asagidaki iki ek + refRx son alternatifi bunu kapatir.
$viewDefRx    = [regex]'(?m)\b(\w+)\s*=\s*\$?"v_Flat_'
$viewConcatRx = [regex]'(?m)\b(\w+)\s*=\s*"v_Flat_"\s*\+'

$refRx = [regex]'(?is)\b(FROM|JOIN|UPDATE|DELETE\s+FROM|INSERT\s+INTO|MERGE(?:\s+INTO)?)\s+(?:\{(_\w+)\}|(?:\[?dbo\]?\.)?\[(\w+)\]|(?:\[?dbo\]?\.)(\w+)|(?:\[[^\]]*\]\.)?\[\{(\w+)\}\])'

foreach ($dir in $srcDirs) {
    foreach ($file in Get-ChildItem $dir -Recurse -Filter *.cs -File) {
        $text = Get-Content $file.FullName -Raw
        if ($text -notmatch 'SELECT|UPDATE|DELETE|INSERT') { continue }
        $scanned++

        $varMap = @{}
        foreach ($d in $varDefRx.Matches($text)) { $varMap[$d.Groups[1].Value] = $d.Groups[2].Value }
        # Duz gorunum degiskenleri: gercek ad calisma zamaninda kurulur; hepsi tek
        # mantiksal ad altinda toplanir ki rapor okunabilir kalsin.
        foreach ($d in $viewDefRx.Matches($text))    { $varMap[$d.Groups[1].Value] = 'v_Flat_*' }
        foreach ($d in $viewConcatRx.Matches($text)) { $varMap[$d.Groups[1].Value] = 'v_Flat_*' }

        foreach ($m in $refRx.Matches($text)) {
            # XML-doc yorumu (///) calistirilabilir SQL degil, anlatim metnidir.
            # Ornek: ILogisticsConfigurationRepository 'Soft delete - UPDATE [BOM] SET...'
            # Bunlari raporlamak kalici yanlis pozitif birakir.
            $lineStart = $text.LastIndexOf("`n", $m.Index) + 1
            if ($lineStart -gt 0 -and $text.Substring($lineStart, $m.Index - $lineStart).TrimStart().StartsWith('///')) { continue }
            $tbl = $null
            if     ($m.Groups[2].Success) { $tbl = $varMap[$m.Groups[2].Value] }
            elseif ($m.Groups[3].Success) { $tbl = $m.Groups[3].Value }
            elseif ($m.Groups[4].Success) { $tbl = $m.Groups[4].Value }
            elseif ($m.Groups[5].Success) { $tbl = $varMap[$m.Groups[5].Value] }
            if (-not $tbl) { continue }
            if ($globalTables -contains $tbl) { continue }
            if ($Table -and $tbl -ne $Table) { continue }
            if ($tbl -like '#*' -or $tbl -like 'sys*' -or $tbl -like 'INFORMATION_SCHEMA*') { continue }
            $verb = $m.Groups[1].Value.ToUpperInvariant() -replace '\s+',' '
            $isMutation = $verb -match 'UPDATE|DELETE|INSERT|MERGE'
            if ($MutationsOnly -and -not $isMutation) { continue }

            # 8000: uzun SET listeli UPDATE'lerde WHERE bu pencerenin disinda kalabiliyordu
            # (SqlAssetRepository.UpdateAssetAsync 25 kolon set ediyor) -> dogru yazilmis
            # ifade 'WHERE yok' diye raporlaniyordu. Genisletmek guvenli: asagidaki
            # IndexOf(';') ifadeyi zaten sonlandirir, komsu sorguya tasmaz.
            $tail = $text.Substring($m.Index, [Math]::Min(8000, $text.Length - $m.Index))
            $stop = $tail.IndexOf(';')
            if ($stop -gt 0) { $tail = $tail.Substring(0, $stop) }

            # Bastirma: ifadenin YAKININDA `tenant-ok:` isaretleyicisi varsa atlanir.
            #   // tenant-ok: sayac kuralin cocugu, kural CompanyId ile suzuluyor
            # Neden gerekli: bazi sorgular EBEVEYNI uzerinden suzulur ve statik tarama bunu
            # goremez. Kalici yanlis pozitif birakmak tarayiciya olan guveni yipratir —
            # herkes 'zaten hep kirmizi' der ve gercek bulgu gozden kacar. Gerekce KODUN
            # yaninda durur, uzak bir listede degil.
            $ctxStart = [Math]::Max(0, $m.Index - 900)   # isaretleyici ile SQL arasi genis olabilir
            $ctx = $text.Substring($ctxStart, $m.Index - $ctxStart) + $tail
            if ($ctx -match 'tenant-ok:') { continue }

            # INSERT'in WHERE'i OLMAZ; dogru soru "sahibi yaziliyor mu" — yani kolon
            # listesinde CompanyId var mi. Eskiden hepsi 'WHERE yok' diye isaretleniyor,
            # dogru yazilmis INSERT'ler de listeyi sisiriyordu.
            # MERGE hem okur hem yazar; dogru soru "ON kosulu sirkete gore mi esliyor".
            # ON'da CompanyId yoksa BASKA sirketin ayni kodlu kaydiyla eslesip onu GUNCELLER.
            # Tarayicinin ilk surumleri MERGE'i HIC gormuyordu (desende yoktu) — 21 yazma
            # yolu gorunmezdi; bir ajan elle bulunca ortaya cikti.
            if ($verb -match 'MERGE') {
                $onIdx = [regex]::Match($tail, '(?i)\bON\b')
                $whenIdx = [regex]::Match($tail, '(?i)\bWHEN\b')
                if ($onIdx.Success -and $whenIdx.Success -and $whenIdx.Index -gt $onIdx.Index) {
                    $onClause = $tail.Substring($onIdx.Index, $whenIdx.Index - $onIdx.Index)
                    if ($onClause -match 'CompanyId') { continue }
                    $reason = 'MERGE, ON kosulunda CompanyId yok'
                } else {
                    if ($tail -match 'CompanyId') { continue }
                    $reason = 'MERGE, CompanyId gecmiyor'
                }
            }
            elseif ($m.Groups[1].Value -match '(?i)INSERT') {
                if ($tail -match 'CompanyId') { continue }
                $reason = 'INSERT, CompanyId yazilmiyor'
            }
            else {
                # SUZGEC yalniz WHERE'den SONRA aranir.
                $w = [regex]::Match($tail, '(?i)\bWHERE\b')
                if (-not $w.Success) {
                    $reason = 'WHERE yok'
                } elseif ($tail.Substring($w.Index) -match 'CompanyId') {
                    continue
                } else {
                    $reason = 'WHERE var, CompanyId yok'
                }
            }

            $findings.Add([pscustomobject]@{
                File   = $file.FullName.Substring($root.Length + 1)
                Line   = ($text.Substring(0, $m.Index) -split "`n").Count
                Table  = $tbl
                Verb   = $verb
                Reason = $reason
                Snip   = ($tail -split "`n" | Select-Object -First 1).Trim()
            })
        }
    }
}

Write-Host ""
Write-Host "Kiraci suzgeci denetimi — $scanned dosya tarandi" -ForegroundColor Cyan
Write-Host ("-" * 76)

if ($findings.Count -eq 0) {
    Write-Host "Bulgu yok." -ForegroundColor Green
    exit 0
}

$findings | Group-Object Table | Sort-Object Count -Descending | ForEach-Object {
    $noWhere = @($_.Group | Where-Object { $_.Reason -eq 'WHERE yok' }).Count
    Write-Host ("{0,-28} {1,4} ifade  (WHERE'i hic olmayan: {2})" -f $_.Name, $_.Count, $noWhere) -ForegroundColor Yellow
    if ($Detail) {
        $_.Group | Sort-Object File, Line | ForEach-Object {
            Write-Host ("      {0}:{1}  [{2}] {3}" -f $_.File, $_.Line, $_.Verb, $_.Reason) -ForegroundColor DarkGray
            Write-Host ("        {0}" -f $_.Snip) -ForegroundColor DarkGray
        }
    }
}

Write-Host ("-" * 76)
$tblCount = @($findings | Select-Object -ExpandProperty Table -Unique).Count
Write-Host ("TOPLAM {0} ifade CompanyId sartindan yoksun ({1} farkli tablo)." -f $findings.Count, $tblCount) -ForegroundColor Yellow
$mut = @($findings | Where-Object { $_.Verb -match 'UPDATE|DELETE|INSERT' }).Count
Write-Host ("Bunlarin {0} tanesi YAZMA yolu (UPDATE/DELETE/INSERT) — oncelik orada." -f $mut) -ForegroundColor Yellow
Write-Host "Her satir bir bug DEGIL: 'WHERE Id = @Id' ile tek kayit okuyanlar zaten guvenli"
Write-Host "sayilabilir (PK benzersiz)."
exit 1
