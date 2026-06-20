; CalibraHub Setup Script
; Inno Setup 6.x ile derlenmelidir: https://jrsoftware.org/isdl.php

#define AppName "CalibraHub"
#ifndef AppVersion
  #define AppVersion "1.7.0"
#endif
#define AppPublisher "CalibraHub"
#define WebServiceName "CalibraHub Web"
#define WorkerServiceName "CalibraHub Worker"
#define GrafanaServiceName "CalibraHub Grafana"
#define WhatsAppServiceName "CalibraHubWhatsAppBridge"

[Setup]
AppId={{A3F7B2C1-4D8E-4F9A-B6C3-2E1D0A5F8B7C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; Setup exe ciktisi proje kokundeki publish/ klasoru
OutputDir=D:\Projeler\Setup\CalibraHub
OutputBaseFilename=CalibraHub-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
UninstallDisplayName={#AppName}
CloseApplications=yes
; Inno Setup'in native log dosyasi — %TEMP%\Setup Log YYYY-MM-DD #N.txt
; Custom log ile birlikte kullaniciya verilebilir.
SetupLogging=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Dirs]
Name: "{app}\Web"
Name: "{app}\Worker"
; Name: "{app}\ServiceManager"  ; 2026-06-19: ServiceManager kaldirildi
Name: "{app}\Logs"
Name: "{app}\GrafanaSetup"
Name: "{app}\WhatsAppBridge"
Name: "{app}\WhatsAppSetup"
Name: "{app}\DependenciesSetup"

[Files]
Source: "..\publish\Web\*"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\Worker\*"; DestDir: "{app}\Worker"; Flags: ignoreversion recursesubdirs createallsubdirs
; 2026-06-19: ServiceManager KALDIRILDI — bagimsiz WinForms tray app projeden cikti.
; Servis yonetimi services.msc / `sc` komutu ile yapilir; ileride Web icine Admin sayfasi entegre edilebilir.
; Source: "..\publish\ServiceManager\*"; DestDir: "{app}\ServiceManager"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Servis ACL grant scripti (post-install [Run] adiminda calisir)
Source: "grant-service-acl.ps1"; DestDir: "{app}"; Flags: ignoreversion
; 2026-06-19: Grafana KALDIRILDI — yeni Rapor Tasarimcisi + Pano arayuzu yerini aldi.
; grafana\*.ps1, *.template, grafana-*.zip dosyalari installer'a paketlenmiyor.
; Eski kurulumlarda kalan GrafanaSetup klasoru runtime'da yok sayilir; servis varsa
; .iss Run section'inda sc stop ile durdurulup atlanir.
; Source: "grafana\*.ps1";       DestDir: "{app}\GrafanaSetup"; Flags: ignoreversion
; Source: "grafana\*.template";  DestDir: "{app}\GrafanaSetup"; Flags: ignoreversion
; Source: "grafana\grafana-*.zip"; DestDir: "{app}\GrafanaSetup"; Flags: ignoreversion skipifsourcedoesntexist
; 2026-06-20: WhatsApp setup scriptleri kaldirildi (installer\whatsapp\ klasoru artik yok).
; Eski installer-side bootstrap script'leri yerine Bridge kendi package.json'i ile npm install yapar
; (post-install [Run] adimi WhatsApp Bridge klasorunde dogrudan npm install cagirir).
; Source: "whatsapp\*"; DestDir: "{app}\WhatsAppSetup"; Flags: ignoreversion recursesubdirs createallsubdirs
; WhatsApp Bridge kaynak kodu (npm install kurulum sirasinda calisir)
Source: "..\publish\WhatsAppBridge\*"; DestDir: "{app}\WhatsAppBridge"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Bagimlilik (.NET 10 + Node.js) check & install scripti + bundled hosting bundle.
; dependencies\dotnet-hosting-10-win.exe build-installer.ps1 tarafindan cache'lenir;
; dosya yoksa Inno warning verir ve install-dependencies.ps1 internet fallback'i kullanir.
Source: "dependencies\*"; DestDir: "{app}\DependenciesSetup"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName} - Tarayicida Ac"; Filename: "{app}\Web\CalibraHub.Web.exe"
; 2026-06-19: ServiceManager kisayollari kaldirildi (proje cikarildi).
; Name: "{group}\{#AppName} Servis Yoneticisi"; Filename: "{app}\ServiceManager\CalibraHub.ServiceManager.exe"; WorkingDir: "{app}\ServiceManager"
; Name: "{commondesktop}\{#AppName} Servis Yoneticisi"; Filename: "{app}\ServiceManager\CalibraHub.ServiceManager.exe"; WorkingDir: "{app}\ServiceManager"; Tasks: desktopicon
Name: "{group}\{#AppName} Kaldır"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "{#AppName} Servis Yoneticisi için Masaüstü kısayolu oluştur"; GroupDescription: "Ek kısayollar:"; Flags: unchecked

[Registry]
; Versiyon ve port bilgileri (kaldirma + upgrade detection icin)
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "Version";       ValueData: "{#AppVersion}"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "InstallPath";   ValueData: "{app}";          Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "WebPort";       ValueData: "";               Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "GrafanaPort";   ValueData: ""
Root: HKLM; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "WhatsAppPort";  ValueData: ""
; Veritabani baglanti bilgileri — upgrade icin saklanir; uninstall'da subkey komple silinir.
; EncryptedString DPAPI-LocalMachine ile sifrelenmis tam connection string.
; Server/Database/AuthMode/Username plain — sadece UI prefill icin.
Root: HKLM; Subkey: "Software\{#AppName}\DbConnection"; Flags: uninsdeletekey

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""{#WebServiceName}"""; Flags: runhidden; RunOnceId: "StopWebSvc"
Filename: "sc.exe"; Parameters: "stop ""{#WorkerServiceName}"""; Flags: runhidden; RunOnceId: "StopWorkerSvc"
Filename: "sc.exe"; Parameters: "stop ""{#GrafanaServiceName}"""; Flags: runhidden; RunOnceId: "StopGrafanaSvc"
Filename: "sc.exe"; Parameters: "stop ""{#WhatsAppServiceName}"""; Flags: runhidden; RunOnceId: "StopWhatsAppSvc"
; WhatsApp servis kaldirma (node-windows cleanup) — {{ ve }} = literal { } Inno Setup escape
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""cd '{app}\WhatsAppBridge'; if (Test-Path 'uninstall-service.js') {{ node uninstall-service.js }}"""; Flags: runhidden; RunOnceId: "UninstallWhatsAppNode"
Filename: "sc.exe"; Parameters: "delete ""{#WebServiceName}"""; Flags: runhidden; RunOnceId: "DelWebSvc"
Filename: "sc.exe"; Parameters: "delete ""{#WorkerServiceName}"""; Flags: runhidden; RunOnceId: "DelWorkerSvc"
Filename: "sc.exe"; Parameters: "delete ""{#GrafanaServiceName}"""; Flags: runhidden; RunOnceId: "DelGrafanaSvc"
Filename: "sc.exe"; Parameters: "delete ""{#WhatsAppServiceName}"""; Flags: runhidden; RunOnceId: "DelWhatsAppSvc"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName} Web"""; Flags: runhidden; RunOnceId: "DelFirewall"

[Code]
// ── Custom wizard pages: bagimliliklar, port'lar, veritabani ──────────
var
  PortPage: TInputQueryWizardPage;
  DependenciesPage: TOutputMsgMemoWizardPage;
  ExistingVersion: String;
  IsUpgrade: Boolean;
  NeedsDotNet: Boolean;
  NeedsNode: Boolean;
  // ── Custom install log ──
  // {app}\Logs\install-YYYYMMDD-HHMMSS.log — her major adim + exit code + servis
  // state polling + Event Log dump. Kurulum sorunlarinda kullaniciya bu dosya
  // gosterilir; tek dosyayla diagnostic yapilabilir.
  InstallLogPath: String;
  InstallHadError: Boolean;

  // ── Veritabani ayarlari sayfasi ──
  // Custom wizard page: TWizardPage uzerine TNewEdit / TNewRadioButton kontrolleri.
  // Inno Setup InputQuery sayfasi password masking + radio destegi vermedigi icin
  // CreateCustomPage + manuel control yerlestirmesi gerekiyor.
  DbPage: TWizardPage;
  DbServerEdit: TNewEdit;
  DbNameEdit:   TNewEdit;
  DbAuthCombo:  TNewComboBox;
  DbUserEdit:   TNewEdit;
  DbPassEdit:   TNewEdit;
  DbTestBtn:    TNewButton;
  DbTestLabel:  TNewStaticText;
  DbUserLabel:  TNewStaticText;
  DbPassLabel:  TNewStaticText;
  // Upgrade durumunda registry'de dpapi:-prefix'li EncryptedString varsa
  // bu sayfa atlanir; mevcut conn string aynen appsettings.json'a yazilir.
  SkipDbWizard: Boolean;
  // Upgrade'de registry'den okunan EncryptedString — CurStepChanged icinde
  // EncryptWithDPAPI'yi tekrar cagirmamak icin saklanir.
  ExistingEncConnStr: String;

// ── Custom install log helper'lari ──
// {app}\Logs\install-YYYYMMDD-HHMMSS.log dosyasina satir satir yazar; her major
// adimi + Exec exit code'unu + servis state'ini kayit eder. Kurulum bitiminde
// kullaniciya yol gosterilir, sorun varsa bu dosya paylasilabilir.

procedure InitInstallLog;
begin
  if InstallLogPath <> '' then Exit;
  ForceDirectories(ExpandConstant('{app}\Logs'));
  InstallLogPath := ExpandConstant('{app}\Logs\install-') +
                    GetDateTimeString('yyyymmdd-hhnnss', #0, #0) + '.log';
  SaveStringToFile(InstallLogPath,
    '=== CalibraHub Setup v{#AppVersion} install log ===' + #13#10, False);
end;

procedure LogLine(const Msg: String);
var
  TS: String;
begin
  if InstallLogPath = '' then InitInstallLog;
  TS := GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':');
  SaveStringToFile(InstallLogPath, '[' + TS + '] ' + Msg + #13#10, True);
end;

procedure LogStep(const Msg: String);
begin
  LogLine('--- ' + Msg + ' ---');
end;

procedure LogError(const Msg: String);
begin
  LogLine('[ERROR] ' + Msg);
  InstallHadError := True;
end;

// sc.exe gibi cli komutlari icin exit code'lu wrapper
procedure ExecLogged(const ExeFile, Args, Tag: String);
var
  ResultCode: Integer;
begin
  Exec(ExeFile, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
    LogLine('  [' + Tag + '] OK (exit=0): ' + ExeFile + ' ' + Args)
  else
    LogError('[' + Tag + '] FAIL (exit=' + IntToStr(ResultCode) + '): ' + ExeFile + ' ' + Args);
end;

// Windows servisinin mevcut durumunu PowerShell Get-Service ile sorgular.
// 'Running' / 'Stopped' / 'StartPending' / 'NOTFOUND' / 'UNKNOWN' donus.
function GetServiceState(const SvcName: String): String;
var
  TempScript, TempOut, Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
begin
  Result := 'UNKNOWN';
  TempScript := ExpandConstant('{tmp}\ch_svcstate.ps1');
  TempOut    := ExpandConstant('{tmp}\ch_svcstate.txt');

  Script :=
    'try {' + #13#10 +
    '  $s = Get-Service -Name "' + SvcName + '" -ErrorAction Stop' + #13#10 +
    '  $s.Status.ToString() | Out-File -FilePath "' + TempOut + '" -Encoding ASCII -NoNewline' + #13#10 +
    '} catch {' + #13#10 +
    '  "NOTFOUND" | Out-File -FilePath "' + TempOut + '" -Encoding ASCII -NoNewline' + #13#10 +
    '}';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringsFromFile(TempOut, Lines) and (GetArrayLength(Lines) > 0) then
    Result := Trim(Lines[0]);
end;

// Servisin RUNNING durumuna gelmesini bekler (default 30 sn). Her 2 sn'de
// bir state poll'lar. Loga her poll sonucunu yazar.
function WaitForServiceRunning(const SvcName: String; TimeoutSec: Integer): Boolean;
var
  Elapsed: Integer;
  State: String;
begin
  Result := False;
  Elapsed := 0;
  while Elapsed < TimeoutSec do
  begin
    State := GetServiceState(SvcName);
    LogLine('  [poll +' + IntToStr(Elapsed) + 's] ' + SvcName + ' durum: ' + State);
    if SameText(State, 'Running') then
    begin
      Result := True;
      Exit;
    end;
    if SameText(State, 'NOTFOUND') then Exit;
    Sleep(2000);
    Elapsed := Elapsed + 2;
  end;
end;

// Servis baslamadiysa Windows Event Log'tan son hatalari install log'una ekler.
procedure DumpEventLogForService(const SvcName: String);
var
  TempScript, TempOut, Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  TempScript := ExpandConstant('{tmp}\ch_evtdump.ps1');
  TempOut    := ExpandConstant('{tmp}\ch_evtdump.txt');

  Script :=
    '$out = "' + TempOut + '"' + #13#10 +
    '"=== System Log (Service Control Manager + ' + SvcName + ') ===" | Out-File $out -Encoding UTF8' + #13#10 +
    'try {' + #13#10 +
    '  Get-WinEvent -FilterHashtable @{LogName="System"; StartTime=(Get-Date).AddHours(-1)} -ErrorAction Stop |' + #13#10 +
    '  Where-Object { ($_.ProviderName -eq "Service Control Manager") -and ($_.Message -match "' + SvcName + '") } |' + #13#10 +
    '  Select-Object -First 10 |' + #13#10 +
    '  Format-List TimeCreated, Id, LevelDisplayName, Message |' + #13#10 +
    '  Out-File $out -Append -Encoding UTF8' + #13#10 +
    '} catch { "(System log okunamadi: $($_.Exception.Message))" | Out-File $out -Append -Encoding UTF8 }' + #13#10 +
    '"" | Out-File $out -Append -Encoding UTF8' + #13#10 +
    '"=== Application Log (Source ' + SvcName + ' / CalibraHub.*) ===" | Out-File $out -Append -Encoding UTF8' + #13#10 +
    'try {' + #13#10 +
    '  Get-WinEvent -FilterHashtable @{LogName="Application"; StartTime=(Get-Date).AddHours(-1)} -ErrorAction Stop |' + #13#10 +
    '  Where-Object { ($_.ProviderName -like "*CalibraHub*") -or ($_.ProviderName -like "*.NET Runtime*") } |' + #13#10 +
    '  Select-Object -First 10 |' + #13#10 +
    '  Format-List TimeCreated, Id, LevelDisplayName, ProviderName, Message |' + #13#10 +
    '  Out-File $out -Append -Encoding UTF8' + #13#10 +
    '} catch { "(Application log okunamadi: $($_.Exception.Message))" | Out-File $out -Append -Encoding UTF8 }';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringsFromFile(TempOut, Lines) then
  begin
    LogLine('=== Event Log dump: ' + SvcName + ' ===');
    for I := 0 to GetArrayLength(Lines) - 1 do
      LogLine(Lines[I]);
    LogLine('=== Event Log dump bitti ===');
  end;
end;

// .NET 10 Runtime kurulu mu? "dotnet --list-runtimes" ciktisinda
// "Microsoft.AspNetCore.App 10." satiri ariyoruz.
function IsDotNet10Installed: Boolean;
var
  TempScript, TempOutput: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TempScript := ExpandConstant('{tmp}\ch_dotnetcheck.ps1');
  TempOutput := ExpandConstant('{tmp}\ch_dotnetcheck.txt');

  Script :=
    '$found = $false' + #13#10 +
    'try { $rt = & dotnet --list-runtimes 2>$null;' + #13#10 +
    '      if ($rt | Where-Object { $_ -match ''Microsoft\.AspNetCore\.App 10\.'' }) { $found = $true } } catch {}' + #13#10 +
    'if ($found) { "YES" | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }' + #13#10 +
    'else        { "NO"  | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Pos('YES', Lines[I]) > 0 then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

// Node.js v18+ kurulu mu?
function IsNodeJsInstalled: Boolean;
var
  TempScript, TempOutput: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TempScript := ExpandConstant('{tmp}\ch_nodecheck.ps1');
  TempOutput := ExpandConstant('{tmp}\ch_nodecheck.txt');

  Script :=
    '$ok = $false' + #13#10 +
    'try {' + #13#10 +
    '  $v = & node --version 2>$null' + #13#10 +
    '  if ($v -match ''^v(\d+)\.'') { if ([int]$Matches[1] -ge 18) { $ok = $true } }' + #13#10 +
    '} catch {}' + #13#10 +
    'if ($ok) { "YES" | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }' + #13#10 +
    'else     { "NO"  | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Pos('YES', Lines[I]) > 0 then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

// Internet baglantisi var mi? aka.ms (Microsoft download CDN) HEAD request'i ile
// kontrol — proxy/firewall'da bile genelde acik, .NET indirmek icin zaten gerek.
// Timeout 5 saniye — yavas baglanti bile bu sureyi asarsa "yok" sayilir.
function IsInternetAvailable: Boolean;
var
  TempScript, TempOutput: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TempScript := ExpandConstant('{tmp}\ch_netcheck.ps1');
  TempOutput := ExpandConstant('{tmp}\ch_netcheck.txt');

  Script :=
    '$ok = $false' + #13#10 +
    'try {' + #13#10 +
    '  [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12' + #13#10 +
    '  $r = Invoke-WebRequest -Uri "https://aka.ms/" -Method Head -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop' + #13#10 +
    '  if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { $ok = $true }' + #13#10 +
    '} catch {' + #13#10 +
    '  # aka.ms erisilemez ise github''i da dene (FastReport Turkish.frl, npm registry vb.)' + #13#10 +
    '  try {' + #13#10 +
    '    $r2 = Invoke-WebRequest -Uri "https://github.com/" -Method Head -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop' + #13#10 +
    '    if ($r2.StatusCode -ge 200 -and $r2.StatusCode -lt 500) { $ok = $true }' + #13#10 +
    '  } catch {}' + #13#10 +
    '}' + #13#10 +
    'if ($ok) { "YES" | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }' + #13#10 +
    'else     { "NO"  | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Pos('YES', Lines[I]) > 0 then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

// Setup baslangicinda — internet sart. Yoksa kullaniciya net mesaj ver ve
// kurulumu iptal et (dependencies, WhatsApp Bridge, FastReport
// Turkish dil paketi — hepsi indirme bazli).
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsInternetAvailable then
  begin
    if MsgBox(
      'Internet baglantisi tespit edilemedi.' + #13#10 + #13#10 +
      'CalibraHub kurulumu su bilesenleri internet uzerinden indirir:' + #13#10 +
      '  • .NET 10 ASP.NET Core Runtime (kurulu degilse)' + #13#10 +
      '  • Node.js LTS (kurulu degilse)' + #13#10 +
      '  • WhatsApp Bridge bagimliliklari / Chromium (~80 MB)' + #13#10 +
      '  • FastReport Turkce dil paketi' + #13#10 + #13#10 +
      'Baglanti olmadan kurulum tamamlanamaz.' + #13#10 + #13#10 +
      'Internet baglantisini kontrol edip tekrar deneyin.' + #13#10 +
      'Yine de devam etmek istiyor musunuz? (Onerilmez)',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

// Port'lari registry'den oku (varsa) — upgrade sirasinda eski degerleri default goster
function ReadPortFromRegistry(ValueName, DefaultPort: String): String;
var
  Value: String;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', ValueName, Value) and
     (Trim(Value) <> '') then
    Result := Value
  else
    Result := DefaultPort;
end;

// Onceki kurulumun versiyonunu oku (upgrade tespiti)
function ReadInstalledVersion: String;
var
  V: String;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'Version', V) then
    Result := V
  else
    Result := '';
end;

// Port kullaniliyor mu? netstat -ano | findstr :PORT — bos cikti = port bos
function IsPortInUse(Port: String): Boolean;
var
  TempScript, TempOutput: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TempScript := ExpandConstant('{tmp}\ch_portcheck.ps1');
  TempOutput := ExpandConstant('{tmp}\ch_portcheck.txt');

  // Test-NetConnection yavas; netstat daha hizli ve guvenilir.
  Script :=
    '$lines = netstat -ano | Select-String -Pattern ":' + Port + '\s" | Select-String -Pattern "LISTENING"' + #13#10 +
    'if ($lines) { "INUSE" | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }' + #13#10 +
    'else        { "FREE"  | Out-File -FilePath "' + TempOutput + '" -Encoding ASCII -NoNewline }';

  SaveStringToFile(TempScript, Script, False);
  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      if Pos('INUSE', Lines[I]) > 0 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

// Port'un sayisal olarak gecerli oldugu kontrol — 1024-65535 arasi
function IsValidPort(PortStr: String): Boolean;
var
  N: Integer;
begin
  N := StrToIntDef(PortStr, -1);
  Result := (N >= 1024) and (N <= 65535);
end;

// ── Veritabani ayarlari registry persistence ──────────────────────────
// Conn string + bilesenler HKLM\Software\CalibraHub\DbConnection altinda.
// EncryptedString DPAPI-LocalMachine ile sifrelenmistir, sadece bu makine cozer.
// Server/Database/AuthMode/Username plain (gizli olmayan) — UI prefill icin.

function ReadDbStringFromRegistry(ValueName: String): String;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}\DbConnection', ValueName, Result) then
    Result := '';
end;

procedure WriteDbStringToRegistry(ValueName, Value: String);
begin
  RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}\DbConnection', ValueName, Value);
end;

// Daha onceki kurulumdan kaydedilmis EncryptedString var mi?
// Varsa upgrade akisinda DbPage atlanir.
function HasSavedDbConfig: Boolean;
begin
  Result := (ReadDbStringFromRegistry('EncryptedString') <> '');
end;

// SQL Server baglantisini test et — PowerShell + System.Data.SqlClient.
// Connection string temp dosyaya yazilir (parolada ' veya " olabilir, escaping zor).
// Sonuc: True = baglandi, False = baglanamadi (hata mesaji ErrMsg ile doner).
function TestDbConnection(ConnStr: String; var ErrMsg: String): Boolean;
var
  TempScript, TempInput, TempOutput: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
  Combined: String;
begin
  Result := False;
  ErrMsg := '';
  TempScript := ExpandConstant('{tmp}\ch_dbtest.ps1');
  TempInput  := ExpandConstant('{tmp}\ch_dbtest_in.txt');
  TempOutput := ExpandConstant('{tmp}\ch_dbtest_out.txt');

  // Conn string'i temp dosyaya yaz — escaping issue'larini bypass eder.
  // master DB'ye baglan: kurulum sirasinda CalibraHub DB henuz olusturulmamis olabilir,
  // ama kullanici credential'larin master'a baglanip CREATE DATABASE yetkisi olmali.
  // Bu yuzden test connection master uzerinden yapilir.
  // ConnStr icindeki Database=X kismini Database=master ile degistirmeliyiz.
  Combined := ConnStr;
  // Database parametresini master'a cevir (kabaca, regex yok PS bunu yapacak)
  SaveStringToFile(TempInput, Combined, False);

  Script :=
    '$cs = [System.IO.File]::ReadAllText("' + TempInput + '").Trim()' + #13#10 +
    '# Database=X --> Database=master (test icin)' + #13#10 +
    '$cs = [regex]::Replace($cs, "(?i)(database|initial catalog)\s*=\s*[^;]+", "Database=master")' + #13#10 +
    '$cs = $cs + ";Connect Timeout=8"' + #13#10 +
    'try {' + #13#10 +
    '  $c = New-Object System.Data.SqlClient.SqlConnection $cs' + #13#10 +
    '  $c.Open()' + #13#10 +
    '  $c.Close()' + #13#10 +
    '  "OK" | Out-File -FilePath "' + TempOutput + '" -Encoding UTF8 -NoNewline' + #13#10 +
    '} catch {' + #13#10 +
    '  $msg = $_.Exception.Message' + #13#10 +
    '  if ($_.Exception.InnerException) { $msg = $_.Exception.InnerException.Message }' + #13#10 +
    '  ("FAIL: " + $msg) | Out-File -FilePath "' + TempOutput + '" -Encoding UTF8 -NoNewline' + #13#10 +
    '}';

  SaveStringToFile(TempScript, Script, False);

  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    if Pos('OK', Lines[0]) = 1 then
    begin
      Result := True;
      Exit;
    end
    else
    begin
      // FAIL mesajini birlestir
      for I := 0 to GetArrayLength(Lines) - 1 do
      begin
        if I = 0 then ErrMsg := Lines[I]
        else ErrMsg := ErrMsg + #13#10 + Lines[I];
      end;
      // "FAIL: " prefix'ini at
      if Pos('FAIL: ', ErrMsg) = 1 then
        Delete(ErrMsg, 1, Length('FAIL: '));
    end;
  end
  else
  begin
    ErrMsg := 'PowerShell test scripti calistirilamadi (exit ' + IntToStr(ResultCode) + ').';
  end;
end;

// Hedef veritabanini olustur (yoksa). master'a baglanip CREATE DATABASE calisir.
// PowerShell scripti ayri dosyaya yazilir — Pascal escape karmasasi yerine
// PowerShell host'unda calisir. DB adi alfanumerik+_ ile sanitize edilir.
// Diagnostik: stdout/stderr ve dosya islemleri loglanir, basarisizlik durumunda
// hata mesaji + log dosya yolu kullaniciya gosterilir.
function EnsureDatabaseExists(ConnStr: String; var ErrMsg: String): Boolean;
var
  TempScript, TempInput, TempOutput, TempLog: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  ErrMsg := '';
  TempScript := ExpandConstant('{tmp}\ch_dbensure.ps1');
  TempInput  := ExpandConstant('{tmp}\ch_dbensure_in.txt');
  TempOutput := ExpandConstant('{tmp}\ch_dbensure_out.txt');
  TempLog    := ExpandConstant('{tmp}\ch_dbensure.log');

  SaveStringToFile(TempInput, ConnStr, False);

  // PowerShell scripti — Pascal'da escape edilen tek karakter `'` (Pascal '' = PS ').
  // Tum diger karakterler PS double-quoted string icinde rahatca yazilir.
  // DB adi -notmatch alfanumerik regex ile sanitize edilir, sonra `[$dbName]` literal
  // SQL'e gomulur (no parameter binding for DDL — SQL Server parameterized DDL'i
  // desteklemez). Sanitize sonrasi enjeksiyon riski yok.
  Script :=
    '$ErrorActionPreference = "Continue"' + #13#10 +
    '$logFile = "' + TempLog + '"' + #13#10 +
    'function Log($m) { try { Add-Content -Path $logFile -Value ("[" + (Get-Date -Format "HH:mm:ss") + "] " + $m) -Encoding UTF8 } catch {} }' + #13#10 +
    'Log "ensure-db basladi"' + #13#10 +
    'try {' + #13#10 +
    '  $cs = [System.IO.File]::ReadAllText("' + TempInput + '").Trim()' + #13#10 +
    '  Log ("ConnStr okundu uzunluk=" + $cs.Length)' + #13#10 +
    '  $dbName = "calibra"' + #13#10 +
    '  if ($cs -match "(?i)(?:database|initial catalog)\s*=\s*([^;]+)") { $dbName = $Matches[1].Trim() }' + #13#10 +
    '  Log ("Hedef DB adi: " + $dbName)' + #13#10 +
    '  # Sanitize — alfanumerik + _ + . (collation/named DBs icin) izin' + #13#10 +
    '  if ($dbName -notmatch ''^[A-Za-z_][A-Za-z0-9_\.]*$'') {' + #13#10 +
    '    Log ("Gecersiz DB adi formati: " + $dbName)' + #13#10 +
    '    ("FAIL: Gecersiz DB adi formati: " + $dbName) | Out-File -FilePath "' + TempOutput + '" -Encoding UTF8 -NoNewline' + #13#10 +
    '    exit 0' + #13#10 +
    '  }' + #13#10 +
    '  $masterCs = [regex]::Replace($cs, "(?i)(database|initial catalog)\s*=\s*[^;]+", "Database=master")' + #13#10 +
    '  $masterCs = $masterCs + ";Connect Timeout=10"' + #13#10 +
    '  Log "master baglantisi aciliyor..."' + #13#10 +
    '  $c = New-Object System.Data.SqlClient.SqlConnection $masterCs' + #13#10 +
    '  $c.Open()' + #13#10 +
    '  Log "master''a baglandi"' + #13#10 +
    '  $cmd = $c.CreateCommand()' + #13#10 +
    '  $cmd.CommandText = "IF DB_ID(N''$dbName'') IS NULL BEGIN CREATE DATABASE [$dbName]; END"' + #13#10 +
    '  Log ("SQL: " + $cmd.CommandText)' + #13#10 +
    '  try {' + #13#10 +
    '    $cmd.ExecuteNonQuery() | Out-Null' + #13#10 +
    '    Log "CREATE DATABASE basarili / DB zaten var"' + #13#10 +
    '  } catch [System.Data.SqlClient.SqlException] {' + #13#10 +
    '    # Yetim .mdf/.ldf dosyalari nedeniyle "already exists" hatasi alindiysa' + #13#10 +
    '    # FOR ATTACH ile dosyalari geri yapistirip DB''yi geri kazandirmaya calis.' + #13#10 +
    '    $errMsg = $_.Exception.Message' + #13#10 +
    '    Log ("CREATE hata (deneme attach): " + $errMsg)' + #13#10 +
    '    if ($errMsg -match "(?i)already exists" -and $errMsg -match "(?i)\.(mdf|ldf|file)") {' + #13#10 +
    '      # Hata mesajindan dosya path''ini cikar' + #13#10 +
    '      $mdfPath = $null; $ldfPath = $null' + #13#10 +
    '      $allMatches = [regex]::Matches($errMsg, "(?i)''([^'']*\.mdf)''")' + #13#10 +
    '      if ($allMatches.Count -gt 0) { $mdfPath = $allMatches[0].Groups[1].Value }' + #13#10 +
    '      $allMatches2 = [regex]::Matches($errMsg, "(?i)''([^'']*\.ldf)''")' + #13#10 +
    '      if ($allMatches2.Count -gt 0) { $ldfPath = $allMatches2[0].Groups[1].Value }' + #13#10 +
    '      # Hata mesajinda mdf/ldf yoksa default data path''den uret' + #13#10 +
    '      if (-not $mdfPath) {' + #13#10 +
    '        $cmd2 = $c.CreateCommand()' + #13#10 +
    '        $cmd2.CommandText = "SELECT CONVERT(NVARCHAR(500), SERVERPROPERTY(''InstanceDefaultDataPath''))"' + #13#10 +
    '        $dataPath = $cmd2.ExecuteScalar()' + #13#10 +
    '        $cmd3 = $c.CreateCommand()' + #13#10 +
    '        $cmd3.CommandText = "SELECT CONVERT(NVARCHAR(500), SERVERPROPERTY(''InstanceDefaultLogPath''))"' + #13#10 +
    '        $logPath = $cmd3.ExecuteScalar()' + #13#10 +
    '        if ($dataPath) { $mdfPath = $dataPath + $dbName + ".mdf" }' + #13#10 +
    '        if ($logPath)  { $ldfPath = $logPath  + $dbName + "_log.ldf" }' + #13#10 +
    '      }' + #13#10 +
    '      # Sadece mdf varsa ldf''yi mdf''ye gore tahmin et' + #13#10 +
    '      if ($mdfPath -and -not $ldfPath) {' + #13#10 +
    '        $ldfPath = [System.IO.Path]::ChangeExtension($mdfPath, "_log.ldf")' + #13#10 +
    '        if (-not (Test-Path $ldfPath)) {' + #13#10 +
    '          $ldfPath = ($mdfPath -replace "\.mdf$", "_log.ldf")' + #13#10 +
    '        }' + #13#10 +
    '      }' + #13#10 +
    '      Log ("Attach denemesi: mdf=" + $mdfPath + " ldf=" + $ldfPath)' + #13#10 +
    '      if ($mdfPath -and (Test-Path $mdfPath)) {' + #13#10 +
    '        $attachCmd = $c.CreateCommand()' + #13#10 +
    '        if ($ldfPath -and (Test-Path $ldfPath)) {' + #13#10 +
    '          $attachCmd.CommandText = "CREATE DATABASE [$dbName] ON (FILENAME=N''$mdfPath''), (FILENAME=N''$ldfPath'') FOR ATTACH"' + #13#10 +
    '        } else {' + #13#10 +
    '          # Sadece mdf var — log dosyasi otomatik yeniden olusturulur' + #13#10 +
    '          $attachCmd.CommandText = "CREATE DATABASE [$dbName] ON (FILENAME=N''$mdfPath'') FOR ATTACH_REBUILD_LOG"' + #13#10 +
    '        }' + #13#10 +
    '        Log ("Attach SQL: " + $attachCmd.CommandText)' + #13#10 +
    '        $attachCmd.ExecuteNonQuery() | Out-Null' + #13#10 +
    '        Log "Attach basarili — yetim dosyalar geri yapistirilarak DB kurtarildi."' + #13#10 +
    '      } else {' + #13#10 +
    '        throw' + #13#10 +
    '      }' + #13#10 +
    '    } else {' + #13#10 +
    '      throw' + #13#10 +
    '    }' + #13#10 +
    '  }' + #13#10 +
    '  $c.Close()' + #13#10 +
    '  ("OK:" + $dbName) | Out-File -FilePath "' + TempOutput + '" -Encoding UTF8 -NoNewline' + #13#10 +
    '  Log "OK ciktisi yazildi"' + #13#10 +
    '} catch {' + #13#10 +
    '  $msg = $_.Exception.Message' + #13#10 +
    '  if ($_.Exception.InnerException) { $msg = $_.Exception.InnerException.Message }' + #13#10 +
    '  Log ("HATA: " + $msg)' + #13#10 +
    '  ("FAIL: " + $msg) | Out-File -FilePath "' + TempOutput + '" -Encoding UTF8 -NoNewline' + #13#10 +
    '}';

  SaveStringToFile(TempScript, Script, False);

  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringsFromFile(TempOutput, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    if Pos('OK:', Lines[0]) = 1 then
    begin
      Result := True;
      Exit;
    end
    else
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
      begin
        if I = 0 then ErrMsg := Lines[I]
        else ErrMsg := ErrMsg + #13#10 + Lines[I];
      end;
      if Pos('FAIL: ', ErrMsg) = 1 then
        Delete(ErrMsg, 1, Length('FAIL: '));
    end;
  end
  else
  begin
    ErrMsg := 'ensure-db scripti cikti olusturmadi (PS exit ' + IntToStr(ResultCode) +
              '). Log: ' + TempLog;
  end;
end;

// SQL Auth mu Windows Auth mu? ComboBox'tan oku.
function IsSqlAuth: Boolean;
begin
  Result := (DbAuthCombo <> nil) and (DbAuthCombo.ItemIndex = 1);
end;

// SQL Server connection string'i page degerlerinden inşa et.
function BuildConnStringFromPage: String;
var
  Server, DbName, UserName, Password: String;
begin
  Server   := Trim(DbServerEdit.Text);
  DbName   := Trim(DbNameEdit.Text);
  UserName := Trim(DbUserEdit.Text);
  Password := DbPassEdit.Text; // Password'da bosluk anlamli
  if Server = '' then Server := 'localhost';
  if DbName = '' then DbName := 'calibra';

  if IsSqlAuth then
    Result := 'Server=' + Server + ';Database=' + DbName + ';User Id=' + UserName +
              ';Password=' + Password + ';TrustServerCertificate=True;'
  else
    Result := 'Server=' + Server + ';Database=' + DbName +
              ';Trusted_Connection=True;TrustServerCertificate=True;';
end;

// ComboBox secimi degisince Username/Password alanlarini goster/gizle
procedure DbAuthChanged(Sender: TObject);
var
  Visible: Boolean;
begin
  Visible := IsSqlAuth();
  DbUserLabel.Visible := Visible;
  DbUserEdit.Visible  := Visible;
  DbPassLabel.Visible := Visible;
  DbPassEdit.Visible  := Visible;
end;

// "Baglantiyi Test Et" butonu tikla
procedure DbTestBtnClick(Sender: TObject);
var
  ConnStr, ErrMsg: String;
  OldCaption: String;
begin
  // Validasyon
  if Trim(DbServerEdit.Text) = '' then
  begin
    DbTestLabel.Caption := 'Sunucu adi bos olamaz.';
    DbTestLabel.Font.Color := clRed;
    Exit;
  end;
  if IsSqlAuth and (Trim(DbUserEdit.Text) = '') then
  begin
    DbTestLabel.Caption := 'SQL Auth icin kullanici adi gerekli.';
    DbTestLabel.Font.Color := clRed;
    Exit;
  end;

  OldCaption := DbTestBtn.Caption;
  DbTestBtn.Caption := 'Test ediliyor...';
  DbTestBtn.Enabled := False;
  DbTestLabel.Caption := '';
  WizardForm.Update;

  ConnStr := BuildConnStringFromPage();

  if TestDbConnection(ConnStr, ErrMsg) then
  begin
    DbTestLabel.Caption := 'Baglanti basarili.';
    DbTestLabel.Font.Color := clGreen;
  end
  else
  begin
    DbTestLabel.Caption := 'Baglanti basarisiz: ' + ErrMsg;
    DbTestLabel.Font.Color := clRed;
  end;

  DbTestBtn.Caption := OldCaption;
  DbTestBtn.Enabled := True;
end;

// Veritabani sayfasini olustur — TNewEdit, TNewComboBox, TNewButton kontrolleri.
// Layout: 2 sutun (Server | Database, Username | Password) — sayfa overflow olmasin.
procedure CreateDbPage(AfterPageId: Integer);
var
  L: TNewStaticText;
  Y: Integer;
  HasSaved: Boolean;
  SavedServer, SavedDb, SavedAuth, SavedUser: String;
  HalfW, GapX, RightX: Integer;
begin
  DbPage := CreateCustomPage(AfterPageId,
    'Veritabani Baglantisi',
    'CalibraHub''in baglanacagi SQL Server bilgilerini girin.');

  HasSaved := HasSavedDbConfig;
  SavedServer := ReadDbStringFromRegistry('Server');
  SavedDb     := ReadDbStringFromRegistry('Database');
  SavedAuth   := ReadDbStringFromRegistry('AuthMode');
  SavedUser   := ReadDbStringFromRegistry('Username');

  GapX   := 12;
  HalfW  := (DbPage.SurfaceWidth - GapX) div 2;
  RightX := HalfW + GapX;

  Y := 4;

  // ── Row 1: Sunucu (sol) + Veritabani (sag) ──
  L := TNewStaticText.Create(DbPage);
  L.Parent := DbPage.Surface;
  L.Caption := 'Sunucu (orn: localhost, .\SQLEXPRESS):';
  L.Top := Y; L.Left := 0; L.AutoSize := True;

  L := TNewStaticText.Create(DbPage);
  L.Parent := DbPage.Surface;
  L.Caption := 'Veritabani Adi:';
  L.Top := Y; L.Left := RightX; L.AutoSize := True;

  Y := Y + L.Height + 2;

  DbServerEdit := TNewEdit.Create(DbPage);
  DbServerEdit.Parent := DbPage.Surface;
  DbServerEdit.Top := Y; DbServerEdit.Left := 0; DbServerEdit.Width := HalfW;
  if HasSaved and (SavedServer <> '') then DbServerEdit.Text := SavedServer
  else DbServerEdit.Text := 'localhost';

  DbNameEdit := TNewEdit.Create(DbPage);
  DbNameEdit.Parent := DbPage.Surface;
  DbNameEdit.Top := Y; DbNameEdit.Left := RightX; DbNameEdit.Width := HalfW;
  if HasSaved and (SavedDb <> '') then DbNameEdit.Text := SavedDb
  else DbNameEdit.Text := 'calibra';

  Y := Y + DbServerEdit.Height + 10;

  // ── Row 2: Auth Mode (full width) ──
  L := TNewStaticText.Create(DbPage);
  L.Parent := DbPage.Surface;
  L.Caption := 'Kimlik Dogrulama Yontemi:';
  L.Top := Y; L.Left := 0; L.AutoSize := True;
  Y := Y + L.Height + 2;

  DbAuthCombo := TNewComboBox.Create(DbPage);
  DbAuthCombo.Parent := DbPage.Surface;
  DbAuthCombo.Top := Y; DbAuthCombo.Left := 0; DbAuthCombo.Width := DbPage.SurfaceWidth;
  DbAuthCombo.Style := csDropDownList;
  DbAuthCombo.Items.Add('Windows Authentication (Trusted Connection — onerilen)');
  DbAuthCombo.Items.Add('SQL Server Authentication (kullanici adi + parola)');
  if HasSaved and (CompareText(SavedAuth, 'sql') = 0) then DbAuthCombo.ItemIndex := 1
  else DbAuthCombo.ItemIndex := 0;
  DbAuthCombo.OnChange := @DbAuthChanged;
  Y := Y + DbAuthCombo.Height + 10;

  // ── Row 3: Username (sol) + Password (sag) — sadece SQL Auth'ta gorunur ──
  DbUserLabel := TNewStaticText.Create(DbPage);
  DbUserLabel.Parent := DbPage.Surface;
  DbUserLabel.Caption := 'Kullanici Adi (orn: sa):';
  DbUserLabel.Top := Y; DbUserLabel.Left := 0; DbUserLabel.AutoSize := True;

  DbPassLabel := TNewStaticText.Create(DbPage);
  DbPassLabel.Parent := DbPage.Surface;
  DbPassLabel.Caption := 'Parola:';
  DbPassLabel.Top := Y; DbPassLabel.Left := RightX; DbPassLabel.AutoSize := True;

  Y := Y + DbUserLabel.Height + 2;

  DbUserEdit := TNewEdit.Create(DbPage);
  DbUserEdit.Parent := DbPage.Surface;
  DbUserEdit.Top := Y; DbUserEdit.Left := 0; DbUserEdit.Width := HalfW;
  if HasSaved then DbUserEdit.Text := SavedUser;

  DbPassEdit := TNewEdit.Create(DbPage);
  DbPassEdit.Parent := DbPage.Surface;
  DbPassEdit.Top := Y; DbPassEdit.Left := RightX; DbPassEdit.Width := HalfW;
  DbPassEdit.PasswordChar := '*'; // Password reserved keyword — PasswordChar kullanilmali

  Y := Y + DbUserEdit.Height + 14;

  // ── Row 4: Test butonu (sol) + sonuc label (sag) — yan yana ──
  DbTestBtn := TNewButton.Create(DbPage);
  DbTestBtn.Parent := DbPage.Surface;
  DbTestBtn.Top := Y; DbTestBtn.Left := 0;
  DbTestBtn.Width := 150; DbTestBtn.Height := 24;
  DbTestBtn.Caption := 'Baglantiyi Test Et';
  DbTestBtn.OnClick := @DbTestBtnClick;

  DbTestLabel := TNewStaticText.Create(DbPage);
  DbTestLabel.Parent := DbPage.Surface;
  DbTestLabel.Caption := '';
  // Test butonun saginda dikey ortali — top'i butonun ortasina hizala
  DbTestLabel.Top := Y + 4;
  DbTestLabel.Left := DbTestBtn.Width + 12;
  DbTestLabel.Width := DbPage.SurfaceWidth - DbTestBtn.Width - 12;
  DbTestLabel.AutoSize := False;
  DbTestLabel.WordWrap := True;
  DbTestLabel.Height := 32;

  // Initial visibility — Auth mode'a gore Username/Password alanlarini goster/gizle
  DbAuthChanged(nil);
end;

procedure InitializeWizard;
var
  VerNote, DepNote: String;
begin
  ExistingVersion := ReadInstalledVersion;
  IsUpgrade := (ExistingVersion <> '') and (ExistingVersion <> '{#AppVersion}');

  if IsUpgrade then
    VerNote := 'Mevcut kurulum: v' + ExistingVersion + '  →  Yeni kurulum: v{#AppVersion} (Yukseltme)'
  else if ExistingVersion = '{#AppVersion}' then
    VerNote := 'Bu surum zaten kurulu: v' + ExistingVersion + ' (Onarim/Tekrar kurulum)'
  else
    VerNote := 'Yeni kurulum: v{#AppVersion}';

  // Bagimlilik tespiti — wizard sayfasinda kullaniciya gostermek icin
  NeedsDotNet := not IsDotNet10Installed;
  NeedsNode   := not IsNodeJsInstalled;

  // Bagimlilik bilgi sayfasi (wpWelcome'dan sonra)
  DepNote := 'CalibraHub asagidaki bilesenlere bagimlidir:' + #13#10 + #13#10;
  if NeedsDotNet then
    DepNote := DepNote + '  ⚠ .NET 10 ASP.NET Core Runtime  → KURULU DEGIL — kurulum sirasinda otomatik yuklenecek' + #13#10
  else
    DepNote := DepNote + '  ✓ .NET 10 ASP.NET Core Runtime  → KURULU' + #13#10;

  if NeedsNode then
    DepNote := DepNote + '  ⚠ Node.js LTS (v18+)             → KURULU DEGIL — kurulum sirasinda otomatik yuklenecek' + #13#10
  else
    DepNote := DepNote + '  ✓ Node.js LTS (v18+)             → KURULU' + #13#10;

  if NeedsDotNet or NeedsNode then
  begin
    DepNote := DepNote + #13#10 +
               'Eksik bilesenler "winget" (Windows Package Manager) ile yuklenmeyi denenir.' + #13#10 +
               'winget yoksa veya basarisiz olursa, dogrudan resmi kurulum dosyalari indirilir.' + #13#10 +
               'Internet baglantisi gerekir; baglanti yoksa kurulum atlanir ve manuel yapilir.' + #13#10 + #13#10 +
               'Devam etmek icin "Ileri" butonuna tiklayin.';
  end
  else
  begin
    DepNote := DepNote + #13#10 +
               'Tum bagimliliklar zaten kurulu. Kurulum sirasinda ek bir indirme yapilmayacak.';
  end;

  DependenciesPage := CreateOutputMsgMemoPage(wpWelcome,
    'Bagimlilik Kontrolu',
    'CalibraHub icin gerekli bilesenler:',
    'Asagidaki ozet kontrolden sonra kurulum klasoru ve port ayarlarina gecilecek.',
    DepNote);

  // Port giris sayfasi — 4 port + versiyon notu
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Servis Portları',
    VerNote,
    'CalibraHub servislerinin dinleyeceği portları belirleyin. ' +
    'Tamamı 61xxx aralığında olmalıdır. Boş portlar otomatik kontrol edilecektir.');
  PortPage.Add('Web (Kestrel) portu:',           False);
  PortPage.Add('WhatsApp Bridge portu:',         False);

  // Default veya mevcut kurulumdan oku
  PortPage.Values[0] := ReadPortFromRegistry('WebPort',      '61001');
  PortPage.Values[1] := ReadPortFromRegistry('WhatsAppPort', '61100');

  // ── Veritabani sayfasi ──
  // 2026-06-01: Sayfa HER ZAMAN gosterilir. Onceki davranis (upgrade'de atla) yanlis
  // kaydedilmis ayarlarin sessizce tasinmasina sebep oluyordu — ozellikle SQL Express
  // / instance adi farkliliklarinda servis bagimliligi bozuluyordu. Artik kullanici
  // her kurulumda DB ayarlarini gorur, registry'den prefill edilir, dogruysa direkt
  // "Ileri" ile gecer. Encrypted connection string ise her kurulumda yeniden uretilir.
  SkipDbWizard := False;
  ExistingEncConnStr := '';
  CreateDbPage(PortPage.ID);
end;

// Upgrade'de DbPage atlanir — kullanici DB ayarlarini tekrar girmek zorunda kalmaz.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (DbPage <> nil) and (PageID = DbPage.ID) and SkipDbWizard then
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  PortStr: String;
  Labels: array[0..1] of String;
  IgnoreInUse: Boolean;
  ConnStr, ErrMsg, Server, DbName: String;
begin
  Result := True;

  // ── Veritabani sayfa validasyonu ──
  if (DbPage <> nil) and (CurPageID = DbPage.ID) then
  begin
    Server := Trim(DbServerEdit.Text);
    DbName := Trim(DbNameEdit.Text);

    if Server = '' then
    begin
      MsgBox('Sunucu adi bos olamaz.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if DbName = '' then
    begin
      MsgBox('Veritabani adi bos olamaz.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if IsSqlAuth then
    begin
      if Trim(DbUserEdit.Text) = '' then
      begin
        MsgBox('SQL Authentication seciliyse Kullanici Adi gereklidir.', mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;

    // Test edilmemisse kullaniciya sor (zorunlu degil ama uyari)
    ConnStr := BuildConnStringFromPage();
    if not TestDbConnection(ConnStr, ErrMsg) then
    begin
      if MsgBox(
        'SQL Server baglantisi test edilemedi:' + #13#10 + #13#10 +
        ErrMsg + #13#10 + #13#10 +
        'Kuruluma devam etmek istiyor musunuz? (Bilgileri sonradan duzeltebilirsiniz.)',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
    Exit;
  end;

  if CurPageID <> PortPage.ID then Exit;

  Labels[0] := 'Web';
  Labels[1] := 'WhatsApp';

  // 1) Validation: hepsi sayisal ve 1024-65535 arasi
  for I := 0 to 1 do
  begin
    PortStr := Trim(PortPage.Values[I]);
    if not IsValidPort(PortStr) then
    begin
      MsgBox(Labels[I] + ' portu gecersiz: "' + PortStr + '". 1024-65535 arasinda bir sayi girin.',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // 2) Cakisan port (ayni numara birden fazla yerde) kontrol
  for I := 0 to 1 do
  begin
    if (I < 1) and (PortPage.Values[I] = PortPage.Values[I + 1]) then
    begin
      MsgBox('Aynı port iki farklı serviste kullanılamaz: ' + PortPage.Values[I],
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // 3) Port kullanim kontrolu — kullaniciya devam secimi sun (mevcut kurulum ayni portu
  //    kullaniyor olabilir, durumda zaten "INUSE" goruluyor)
  IgnoreInUse := False;
  for I := 0 to 1 do
  begin
    PortStr := PortPage.Values[I];
    if IsPortInUse(PortStr) then
    begin
      if not IgnoreInUse then
      begin
        if MsgBox(Labels[I] + ' portu (' + PortStr + ') su anda kullaniliyor.' + #13#10 + #13#10 +
                  'Eger mevcut CalibraHub kurulumunu yukseltiyorsaniz bu beklenen durumdur — ' +
                  'servis durdurulup yeniden baslatilacaktir.' + #13#10 + #13#10 +
                  'Devam edilsin mi? (Hayir = port degistir)',
                  mbConfirmation, MB_YESNO) = IDNO then
        begin
          Result := False;
          Exit;
        end;
        IgnoreInUse := True;  // sonraki portlar icin tekrar sorma
      end;
    end;
  end;
end;

// ── Helper'lar (DPAPI, appsettings patch, service create) ──────────────

// Post-install adimlarinda (.NET indirme, Grafana, WhatsApp npm install) progress
// bar dolu ama uzun sureli operasyon devam ediyor — kullaniciya hangi adimda
// oldugumuzu bildiren live status yazisi.
procedure SetInstallStatus(MainMsg, SubMsg: String);
begin
  if WizardForm = nil then Exit;
  WizardForm.StatusLabel.Caption := MainMsg;
  WizardForm.FilenameLabel.Caption := SubMsg;
  WizardForm.Update;
end;

function GetWebPort:      String; begin Result := PortPage.Values[0]; end;
function GetWhatsAppPort: String; begin Result := PortPage.Values[1]; end;

function GetPlainConnectionString: String;
begin
  // DbPage atlandiysa (upgrade) — registry'den encrypted string'i kullanacagiz,
  // bu fonksiyon plain donmek zorunda degil. Kontrol cagrisinda zaten check edilir.
  if (DbPage <> nil) and (not SkipDbWizard) then
    Result := BuildConnStringFromPage()
  else
    // Fallback — registry'de bilesenler varsa onu, yoksa default'i kullan.
    Result := 'Server=localhost;Database=calibra;Trusted_Connection=True;TrustServerCertificate=True;';
end;

// Windows DPAPI ile baglanti dizesini sifrele.
function EncryptWithDPAPI(PlainText: String): String;
var
  TempInput, TempOutput, TempScript: String;
  Script: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
begin
  TempInput  := ExpandConstant('{tmp}\ch_in.txt');
  TempOutput := ExpandConstant('{tmp}\ch_out.txt');
  TempScript := ExpandConstant('{tmp}\ch_enc.ps1');

  SaveStringToFile(TempInput, PlainText, False);

  Script :=
    'Add-Type -AssemblyName System.Security' + #13#10 +
    '$plain = [System.IO.File]::ReadAllText("' + TempInput + '", [System.Text.Encoding]::UTF8)' + #13#10 +
    '$bytes = [System.Text.Encoding]::UTF8.GetBytes($plain)' + #13#10 +
    '$enc   = [System.Security.Cryptography.ProtectedData]::Protect(' + #13#10 +
    '           $bytes, $null,' + #13#10 +
    '           [System.Security.Cryptography.DataProtectionScope]::LocalMachine)' + #13#10 +
    '[System.IO.File]::WriteAllText("' + TempOutput + '", "dpapi:" + [System.Convert]::ToBase64String($enc),' + #13#10 +
    '    [System.Text.Encoding]::ASCII)';

  SaveStringToFile(TempScript, Script, False);

  Exec('powershell.exe',
       '-ExecutionPolicy Bypass -NonInteractive -File "' + TempScript + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (ResultCode = 0) and
     LoadStringsFromFile(TempOutput, Lines) and
     (GetArrayLength(Lines) > 0) and
     (Pos('dpapi:', Lines[0]) = 1) then
    Result := Lines[0]
  else
  begin
    MsgBox('Uyarı: Şifre şifrelenemedi, düz metin olarak kaydedilecek.', mbInformation, MB_OK);
    Result := PlainText;
  end;
end;

procedure PatchWebAppSettings(FilePath, EncConnStr, Port, WhatsAppPort: String);
var
  Lines: TArrayOfString;
  I: Integer;
  InKestrel, KestrelUrlPatched: Boolean;
begin
  if not LoadStringsFromFile(FilePath, Lines) then Exit;
  // Context-aware patch — "Url" alani appsettings.json'da birden fazla yerde
  // gecebilir (Kestrel.Endpoints.Http.Url + Grafana.Url). Sadece Kestrel
  // block'undaki ilk "Url" alanini degistir; digerleri (Grafana) korunur.
  InKestrel := False;
  KestrelUrlPatched := False;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos('"ConnectionString"', Lines[I]) > 0 then
      Lines[I] := '    "ConnectionString": "' + EncConnStr + '",';

    // Kestrel section tespiti
    if Pos('"Kestrel"', Lines[I]) > 0 then
      InKestrel := True;

    // Sadece Kestrel kapsaminda ve henuz patch edilmemis "Url"
    if InKestrel and (not KestrelUrlPatched) and (Pos('"Url"', Lines[I]) > 0) then
    begin
      Lines[I] := '        "Url": "http://0.0.0.0:' + Port + '"';
      KestrelUrlPatched := True;
    end;

    // WhatsApp Bridge URL — farkli isim, isim bazli match guvenli
    if Pos('"WebQrBridgeUrl"', Lines[I]) > 0 then
      Lines[I] := '    "WebQrBridgeUrl": "http://127.0.0.1:' + WhatsAppPort + '",';
  end;
  SaveStringsToFile(FilePath, Lines, False);
end;

procedure PatchWorkerAppSettings(FilePath, EncConnStr: String);
var
  Lines: TArrayOfString;
  I: Integer;
begin
  if not LoadStringsFromFile(FilePath, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos('"ConnectionString"', Lines[I]) > 0 then
      Lines[I] := '    "ConnectionString": "' + EncConnStr + '",';
  end;
  SaveStringsToFile(FilePath, Lines, False);
end;

// Connection string'deki Server bilgisinden Windows servis adini turet.
// 'localhost'              -> 'MSSQLSERVER'  (default instance)
// '.\SQLEXPRESS'           -> 'MSSQL$SQLEXPRESS'
// 'localhost\NAMEDINSTANCE'-> 'MSSQL$NAMEDINSTANCE'
// '192.168.1.10' veya uzak host -> '' (depend= eklenmez; uzak SQL local servisi yok)
function GetLocalSqlServiceName(ServerSpec: String): String;
var
  S, HostPart, Instance: String;
  P: Integer;
begin
  Result := '';
  S := LowerCase(Trim(ServerSpec));
  if S = '' then
  begin
    Result := 'MSSQLSERVER';
    Exit;
  end;

  P := Pos('\', S);
  if P = 0 then
  begin
    HostPart := S;
    Instance := '';
  end
  else
  begin
    HostPart := Copy(S, 1, P - 1);
    Instance := Copy(S, P + 1, Length(S));
  end;

  // Sadece local host adlari icin Windows servisi bagimliligi anlamli
  if (HostPart <> 'localhost') and (HostPart <> '.') and (HostPart <> '(local)') and
     (HostPart <> '127.0.0.1') and (HostPart <> '') then
    Exit;  // uzak SQL: bagimlilik ekleme

  if Instance = '' then
    Result := 'MSSQLSERVER'
  else
    Result := 'MSSQL$' + UpperCase(Instance);
end;

procedure CreateWindowsService(ServiceName, ExePath, DisplayName, Description, SqlServiceName: String);
var
  ResultCode, QueryCode, I: Integer;
  Deleted: Boolean;
begin
  LogStep('Servis olustur: ' + ServiceName);
  LogLine('  binPath = ' + ExePath);
  LogLine('  SqlDependency = ' + SqlServiceName);

  Exec('sc.exe', 'stop "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogLine('  [stop] exit=' + IntToStr(ResultCode) + ' (mevcut servis durduruldu varsa)');

  // Stop sonrasi kisa bekleme — servisin SERVICE_STOPPED durumuna gecmesi icin
  Sleep(1500);

  Exec('sc.exe', 'delete "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogLine('  [delete] exit=' + IntToStr(ResultCode) + ' (mevcut servis silindi varsa)');

  // SCM "marked for deletion" durumunda kalabilir (services.msc / Event Viewer
  // gibi bir tool servis handle'i tutuyorsa). Polling ile servis nesnesi gercekten
  // silinene kadar bekle (max 30 sn). Beklemezsek 'create' 1072 hatasi verir.
  Deleted := False;
  for I := 1 to 30 do
  begin
    Sleep(1000);
    Exec('sc.exe', 'query "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, QueryCode);
    if QueryCode = 1060 then  // 1060 = ERROR_SERVICE_DOES_NOT_EXIST → silindi
    begin
      LogLine('  [wait-deleted] +' + IntToStr(I) + 's: SCM servis kaydini temizledi');
      Deleted := True;
      Break;
    end;
  end;
  if not Deleted then
    LogLine('  [wait-deleted] WARNING: 30 sn icinde silinmedi (create 1072 verebilir)');

  Exec('sc.exe',
       'create "' + ServiceName + '" binPath= "' + ExePath + '" start= auto DisplayName= "' + DisplayName + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
    LogLine('  [create] OK')
  else
    LogError('  [create] FAIL exit=' + IntToStr(ResultCode));

  Exec('sc.exe',
       'description "' + ServiceName + '" "' + Description + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogLine('  [description] exit=' + IntToStr(ResultCode));
  // SQL Server dependency — boot sirasinda DB hazir olmadan CalibraHub
  // servisleri baslamasin. SqlServiceName connection string'den turetilir:
  //   localhost -> MSSQLSERVER
  //   .\SQLEXPRESS -> MSSQL$SQLEXPRESS
  //   uzak host -> '' (eklenmez; servis sistem boot'unda direkt baslar)
  // Kullanici uzak SQL kullaniyorsa local'de SQL servisi olmadigi icin
  // depend= eklemeyiz; aksi halde 7003 "Bu hizmet yüklü olmayabilir" hatasi olusur.
  if SqlServiceName <> '' then
  begin
    Exec('sc.exe',
         'config "' + ServiceName + '" depend= ' + SqlServiceName,
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode = 0 then
      LogLine('  [config depend= ' + SqlServiceName + '] OK')
    else
      LogError('  [config depend= ' + SqlServiceName + '] FAIL exit=' + IntToStr(ResultCode));
  end
  else
  begin
    // depend= / (slash) tek basina bagimlilik listesini temizler (eski kurulumdan kalmis olabilir)
    Exec('sc.exe',
         'config "' + ServiceName + '" depend= /',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    LogLine('  [config depend= /] exit=' + IntToStr(ResultCode) + ' (bagimlilik temizlendi)');
  end;
  // Crash recovery: 3 kere yeniden basla (5sn arayla, 24h reset)
  Exec('sc.exe',
       'failure "' + ServiceName + '" reset= 86400 actions= restart/5000/restart/5000/restart/10000',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogLine('  [failure recovery] exit=' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PlainConnStr, EncConnStr, WebPort, WhatsAppPort, SqlSvcName, SqlGrantServer: String;
  ResultCode: Integer;
  DepArgs, DbErrMsg: String;
  WebRunning, WorkerRunning: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    InitInstallLog;
    LogStep('Kurulum baslangic — CalibraHub v{#AppVersion}');
    if IsUpgrade then
      LogLine('Mod: UPGRADE (mevcut versiyon: ' + ExistingVersion + ')')
    else
      LogLine('Mod: YENI KURULUM');
    LogLine('NeedsDotNet=' + IntToStr(Ord(NeedsDotNet)) +
            ', NeedsNode=' + IntToStr(Ord(NeedsNode)));

    // ── Bagimlilik kurulumu — Web/Worker servisleri baslamadan ONCE ───────
    // .NET 10 yoksa Web.exe acilmaz. Node.js yoksa WhatsApp Bridge skip olur.
    // Script idempotent: zaten kurulu olanlari atlar.
    if NeedsDotNet or NeedsNode then
    begin
      LogStep('Bagimlilik kurulumu (.NET 10 / Node.js)');
      SetInstallStatus(
        'Bagimlilkar yukleniyor...',
        '.NET 10 ASP.NET Core Runtime ve/veya Node.js LTS indirilip kuruluyor — bu islem 2-5 dakika surebilir.');
      DepArgs := '';
      if not NeedsDotNet then DepArgs := DepArgs + ' -SkipDotNet';
      if not NeedsNode   then DepArgs := DepArgs + ' -SkipNode';
      // Bundled .NET 10 EXE'yi parametre olarak gec — script once bunu dener,
      // basarisiz olursa winget/aka.ms fallback'a duser.
      DepArgs := DepArgs + ' -BundledDotNetExe "' +
                 ExpandConstant('{app}\DependenciesSetup\dotnet-hosting-10-win.exe') + '"';
      Exec('powershell.exe',
           '-ExecutionPolicy Bypass -NonInteractive -File "' +
           ExpandConstant('{app}\DependenciesSetup\install-dependencies.ps1') + '"' +
           DepArgs,
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      LogLine('  install-dependencies.ps1 exit=' + IntToStr(ResultCode));
      LogLine('  (detayli log: %TEMP%\calibrahub-dependencies.log)');
    end
    else
      LogLine('Bagimlilik adimi atlandi (her ikisi de zaten kurulu).');

    WebPort      := GetWebPort();
    WhatsAppPort := GetWhatsAppPort();
    LogLine('Portlar: Web=' + WebPort + ' WhatsApp=' + WhatsAppPort);

    SetInstallStatus(
      'Konfigurasyon hazirlaniyor...',
      'Veritabani baglanti dizesi sifreleniyor (DPAPI) ve appsettings.json guncelleniyor.');

    // ── Connection string: upgrade vs yeni kurulum ──
    // Upgrade ve registry'de DPAPI-encrypted string var → onu kullan, yeniden sifreleme.
    // Yeni kurulum → DbPage degerlerini topla, encrypt et.
    LogStep('Connection string hazirlama');
    if SkipDbWizard and (ExistingEncConnStr <> '') then
    begin
      EncConnStr   := ExistingEncConnStr;
      // Plain string yok (upgrade'de kullanilmaz), bos brakabiliriz.
      PlainConnStr := '';
      LogLine('SkipDbWizard=true → mevcut encrypted string reuse edildi.');
    end
    else
    begin
      PlainConnStr := GetPlainConnectionString();
      EncConnStr   := EncryptWithDPAPI(PlainConnStr);
      LogLine('Server=' + Trim(DbServerEdit.Text) +
              ', Database=' + Trim(DbNameEdit.Text) +
              ', Auth=' + IntToStr(DbAuthCombo.ItemIndex));

      // Bilesenleri ve encrypted string'i registry'ye yaz — sonraki upgrade'de tekrar sormamak icin.
      // Server/Database/AuthMode/Username plain (gizli olmayan) UI prefill icin.
      // EncryptedString DPAPI-LocalMachine ile sifrelenmis, sadece bu makine cozer.
      WriteDbStringToRegistry('Server',   Trim(DbServerEdit.Text));
      WriteDbStringToRegistry('Database', Trim(DbNameEdit.Text));
      if IsSqlAuth then
      begin
        WriteDbStringToRegistry('AuthMode', 'sql');
        WriteDbStringToRegistry('Username', Trim(DbUserEdit.Text));
      end
      else
      begin
        WriteDbStringToRegistry('AuthMode', 'windows');
        WriteDbStringToRegistry('Username', '');
      end;
      WriteDbStringToRegistry('EncryptedString', EncConnStr);

      // ── Database olustur (yoksa) ─────────────────────────────────────────
      // Test connection NextButtonClick'te zaten gectiyse master baglantisi
      // calisir. Burada CREATE DATABASE [<dbName>] IF NOT EXISTS yapiyoruz —
      // boylece Web servis startup'i "Cannot open database" almaz.
      // Fail durumunda kullaniciya net mesaj + setup devam eder (yine de
      // belki manuel olusturmak istiyor olabilir).
      SetInstallStatus(
        'Veritabani olusturuluyor...',
        '"' + Trim(DbNameEdit.Text) + '" veritabani master''a baglanip CREATE DATABASE ile olusturuluyor.');
      if not EnsureDatabaseExists(PlainConnStr, DbErrMsg) then
      begin
        LogError('EnsureDatabaseExists FAIL: ' + DbErrMsg);
        MsgBox(
          '"' + Trim(DbNameEdit.Text) + '" veritabani olusturulamadi:' + #13#10 + #13#10 +
          DbErrMsg + #13#10 + #13#10 +
          'Web servisi baslatildiginda DB''ye baglanamayacak ve hata verecek.' + #13#10 + #13#10 +
          'Olasi nedenler:' + #13#10 +
          '  - Kullanicinin CREATE DATABASE yetkisi yok (sysadmin/dbcreator gerekli)' + #13#10 +
          '  - SQL Server disk dolu' + #13#10 + #13#10 +
          'Kuruluma devam edilecek. SSMS''te manuel olarak veritabanini olusturun:' + #13#10 +
          '  CREATE DATABASE [' + Trim(DbNameEdit.Text) + '];' + #13#10 +
          'Sonra "CalibraHub Servis Yoneticisi" ile Web servisini Restart edin.',
          mbInformation, MB_OK);
      end
      else
        LogLine('EnsureDatabaseExists OK: ' + Trim(DbNameEdit.Text));
    end;

    // appsettings.json dosyalarini guncelle
    PatchWebAppSettings(
      ExpandConstant('{app}\Web\appsettings.json'), EncConnStr, WebPort, WhatsAppPort);
    PatchWorkerAppSettings(
      ExpandConstant('{app}\Worker\appsettings.json'), EncConnStr);

    // Port + versiyon bilgilerini registry'ye yaz
    RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'WebPort',      WebPort);
    RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'WhatsAppPort', WhatsAppPort);
    RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'Version',      '{#AppVersion}');
    RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'InstallPath',  ExpandConstant('{app}'));
    if IsUpgrade and (ExistingVersion <> '') then
      RegWriteStringValue(HKEY_LOCAL_MACHINE, 'Software\{#AppName}', 'PreviousVersion', ExistingVersion);

    // 2026-06-02: ServicesPipeTimeout — Web servisi ilk acilista MVC + Razor + 320
    // endpoint katalog + DB initialization sebebiyle Windows SCM'nin default 30 sn
    // timeout'unu asabiliyor → "1053 Hizmet zamaninda yanit vermedi" hatasi. Bunu
    // 120 sn'ye cikarttiriyoruz. Registry degisikligi reboot gerektirir, ama mevcut
    // kurulum sonrasi servis baslatma da bu yeni timeout'a tabi olur (SCM degerleri
    // anlik degil, servis start'ta okuyor).
    LogStep('ServicesPipeTimeout (Web ilk acilis icin) 120 sn yapiliyor');
    RegWriteDWordValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control', 'ServicesPipeTimeout', 120000);
    LogLine('  HKLM\SYSTEM\CurrentControlSet\Control\ServicesPipeTimeout = 120000 ms');
    LogLine('  NOT: Tam etkili olmasi icin sistem yeniden baslatilmali. Tek seferlik');
    LogLine('  kurulum sonrasi service start denemesi yeterli; degil ise reboot.');

    SetInstallStatus(
      'Windows servisleri olusturuluyor...',
      'CalibraHub Web ve CalibraHub Worker servisleri kayit ediliyor.');

    // SQL servis adini connection string Server bilgisinden turet — default
    // 'MSSQLSERVER' her makinede yok; SQL Express'te 'MSSQL$SQLEXPRESS' olur.
    // Server bilgisini upgrade'de registry'den, yeni kurulumda page'den oku.
    if SkipDbWizard then
      SqlSvcName := GetLocalSqlServiceName(ReadDbStringFromRegistry('Server'))
    else
      SqlSvcName := GetLocalSqlServiceName(Trim(DbServerEdit.Text));
    if SqlSvcName = '' then
      LogLine('SQL servis bagimliligi: (yok — uzak SQL veya tespit edilemedi)')
    else
      LogLine('SQL servis bagimliligi: ' + SqlSvcName);

    // Windows servislerini olustur
    CreateWindowsService(
      '{#WebServiceName}',
      ExpandConstant('{app}\Web\CalibraHub.Web.exe'),
      '{#WebServiceName}',
      'CalibraHub Web Uygulaması - Port ' + WebPort,
      SqlSvcName);

    CreateWindowsService(
      '{#WorkerServiceName}',
      ExpandConstant('{app}\Worker\CalibraHub.Worker.exe'),
      '{#WorkerServiceName}',
      'CalibraHub Arka Plan İşlem Servisi',
      SqlSvcName);

    SetInstallStatus(
      'Guvenlik duvari yapilandirilryor...',
      'Web port (' + WebPort + ') icin gelen baglantilara izin verildi.');

    // Guvenlik duvari kurali (dogru portla)
    Exec('netsh.exe',
         'advfirewall firewall add rule name="{#AppName} Web" dir=in action=allow protocol=TCP localport=' + WebPort,
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    SetInstallStatus(
      'SQL Server yetkilendiriliyor...',
      'NT AUTHORITY\SYSTEM hesabi sysadmin rolune ekleniyor (servisin DB''ye baglanmasi icin).');

    // ── SQL Server'da NT AUTHORITY\SYSTEM yetkilendirmesi ────────────────
    // CalibraHub Web/Worker servisleri default olarak LocalSystem user
    // (NT AUTHORITY\SYSTEM) ile calisir. Trusted_Connection=True ile SQL'e
    // baglanir. SQL Server kurulumunda bu user'a default sysadmin verilmediginde
    // Web startup'ta "Login failed / Cannot open database" hatasiyla crash olur.
    // Bu adim idempotent — login varsa skip eder, sysadmin yetkisini ekler.
    // Sessiz fail olursa devam — kullanici manuel SSMS ile verebilir.
    //
    // 2026-06-02: sqlcmd hedefi DbServerEdit'ten okunur — eski hardcoded "localhost"
    // SQL Express kullaniclara grant'in YANLIS instance'a gitmesine sebep oluyordu.
    // Connection string'in icindeki Server parametresine eslestir.
    //
    // sqlcmd -C: ODBC Driver 18+ default olarak SSL sertifika dogrulamasi yapar.
    // Local SQL Server'da self-signed cert oldugundan -C (TrustServerCertificate)
    // gerekir. Aksi halde "SSL Saglayicisi: Sertifika zinciri guvenilmeyen" hatasi.
    SqlGrantServer := '';
    if SkipDbWizard then
      SqlGrantServer := Trim(ReadDbStringFromRegistry('Server'))
    else
      SqlGrantServer := Trim(DbServerEdit.Text);
    if SqlGrantServer = '' then SqlGrantServer := 'localhost';
    LogLine('SQL grant hedef sunucu: ' + SqlGrantServer);

    Exec('powershell.exe',
         '-ExecutionPolicy Bypass -NonInteractive -Command "$sql = ' +
         '''USE master; IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N''''NT AUTHORITY\SYSTEM'''') CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS; ALTER SERVER ROLE sysadmin ADD MEMBER [NT AUTHORITY\SYSTEM];''' +
         '; $sql | sqlcmd -S """' + SqlGrantServer + '""" -E -C"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    LogLine('  sqlcmd grant NT AUTHORITY\SYSTEM exit=' + IntToStr(ResultCode));
    // Eger sqlcmd basarisiz oldysa (exit !=0), kullaniciya net uyari ver — ama
    // kuruluma devam et. Kullanici sonradan manuel grant edebilir.
    if ResultCode <> 0 then
    begin
      LogError('NT AUTHORITY\SYSTEM grant basarisiz (exit ' + IntToStr(ResultCode) +
               '). Servis startup''inda "Login failed" alacak.');
    end;

    SetInstallStatus(
      'Servisler baslatiliyor...',
      'CalibraHub Web ve Worker servisleri ayaga kalkiyor — DB migration ilk acilista calisir.');

    // Servisleri baslat — SIRALI: Worker once (DB migration yapsin), sonra Web.
    // Aksi halde her ikisi de ayni anda DB init yapip yarisir ve Web SCM'in
    // 30 sn timeout'una takilir (1053 hatasi).
    LogStep('Worker ilk kez baslatiliyor');
    ExecLogged('sc.exe', 'start "{#WorkerServiceName}"', 'first-start Worker');

    LogStep('Worker RUNNING bekleniyor (90 sn timeout) — DB migration tamamlansin diye');
    WorkerRunning := WaitForServiceRunning('{#WorkerServiceName}', 90);

    if WorkerRunning then
      LogLine('Worker RUNNING — Web baslatma ile devam.')
    else
      LogError('Worker RUNNING durumuna gelmedi 90 sn icinde — Web yine de denenecek.');

    // Worker bitirdikten sonra Web baslat — DB hazir, race yok.
    LogStep('Web ilk kez baslatiliyor');
    ExecLogged('sc.exe', 'start "{#WebServiceName}"', 'first-start Web');

    LogStep('Web RUNNING bekleniyor (90 sn timeout)');
    WebRunning := WaitForServiceRunning('{#WebServiceName}', 90);
    LogLine('Worker final: ' + GetServiceState('{#WorkerServiceName}'));
    LogLine('Web    final: ' + GetServiceState('{#WebServiceName}'));

    if not WorkerRunning then
    begin
      LogError('CalibraHub Worker RUNNING durumuna gelmedi — Event Log dump aliniyor');
      DumpEventLogForService('{#WorkerServiceName}');
    end;
    if not WebRunning then
    begin
      LogError('CalibraHub Web RUNNING durumuna gelmedi — Event Log dump aliniyor');
      DumpEventLogForService('{#WebServiceName}');
    end;

    // 2026-06-19: Grafana KALDIRILDI — yeni Rapor Tasarımcısı + Pano arayüzü yerini aldı.
    // grafana-setup.ps1 dosyası varsa eski kurulumda kalmış demektir; mevcut Grafana
    // servisini durdurup install adımını atla. Yoksa hiçbir şey yapma.
    if FileExists(ExpandConstant('{app}\GrafanaSetup\grafana-setup.ps1')) then
    begin
      SetInstallStatus(
        'Eski Grafana kurulumu temizleniyor...',
        'Yeni surumde Grafana kullanilmiyor — varsa onceki servis durdurulup atlanir.');
      Exec('sc.exe', 'stop "CalibraHub Grafana"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    SetInstallStatus(
      'WhatsApp Bridge bagimliliklari yukleniyor...',
      'Node.js (npm install) calisiyor, Chromium indiriliyor (~80 MB) — 2-5 dakika surebilir.');

    // WhatsApp Bridge kurulumu — opsiyonel (Node.js varsa kurar, yoksa skip).
    Exec('powershell.exe',
         '-ExecutionPolicy Bypass -NonInteractive -File "' +
         ExpandConstant('{app}\WhatsAppSetup\whatsapp-setup.ps1') + '"' +
         ' -InstallRoot "' + ExpandConstant('{app}') + '"' +
         ' -WebAppSettingsPath "' + ExpandConstant('{app}\Web\appsettings.json') + '"' +
         ' -BridgePort ' + WhatsAppPort,
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    SetInstallStatus(
      'Servis ACL ayarlari uygulaniyor...',
      'Authenticated Users grubuna servis Start/Stop yetkisi veriliyor (Servis Yoneticisi UAC''siz calissin).');

    // ── Servis ACL grant — UAC sorulmadan ServiceManager calissin ─────────
    // Authenticated Users'a CalibraHub.Web/Worker/Grafana servisleri icin
    // SERVICE_START + SERVICE_STOP + SERVICE_QUERY_STATUS yetkisi verilir.
    // Boylece CalibraHub Servis Yoneticisi (asInvoker manifest) standart
    // kullanici icin de servis baslatip durdurabilir, UAC sormaz.
    Exec('powershell.exe',
         '-ExecutionPolicy Bypass -NonInteractive -File "' +
         ExpandConstant('{app}\grant-service-acl.ps1') + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    SetInstallStatus(
      'Web servisi yeniden baslatiliyor...',
      'Grafana + WhatsApp appsettings degisiklikleri yansisin diye Web restart ediliyor.');

    // ── Servis Disabled korumasi ─────────────────────────────────────────
    // Onceki kurulumda Web/Worker DB sorunu nedeniyle 3+ kez crash etti ise,
    // Windows servisi "Disabled" durumuna cekmis olabilir (recovery 3-fail rule).
    // Burada servisleri zorla "Auto" yapiyoruz — kurulum sonrasi disabled kalmasin.
    Exec('sc.exe', 'config "{#WebServiceName}"    start= auto', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'config "{#WorkerServiceName}" start= auto', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Web servisini restart et — Grafana + WhatsApp appsettings.json patch'lerini okusun
    Exec('sc.exe', 'stop "{#WebServiceName}"',  '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
    Exec('sc.exe', 'start "{#WebServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Worker da Disabled olabilir — start dene (zaten calisiyorsa no-op)
    Exec('sc.exe', 'start "{#WorkerServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // ── Final servis durumu + log raporu ─────────────────────────────────
    LogStep('Final servis durumu (Web restart sonrasi)');
    LogLine('Worker: ' + GetServiceState('{#WorkerServiceName}'));
    LogLine('Web   : ' + GetServiceState('{#WebServiceName}'));
    LogStep('Kurulum tamamlandi');
    if InstallHadError then
      LogLine('SONUC: Kurulum tamamlandi AMA hata(lar) var — yukaridaki [ERROR] satirlarini inceleyin.')
    else
      LogLine('SONUC: Kurulum BASARILI — tum adimlar exit=0.');

    SetInstallStatus(
      'Kurulum tamamlandi',
      'CalibraHub hazir. "Sonraki" butonuna tiklayin.');

    // Kullaniciya log dosyasini bildir — sorun varsa direkt bu dosya paylasilabilir.
    if InstallHadError then
    begin
      MsgBox(
        'Kurulum tamamlandi ANCAK bazi adimlar hata verdi.' + #13#10 + #13#10 +
        'Detayli log dosyasi:' + #13#10 +
        InstallLogPath + #13#10 + #13#10 +
        'Lutfen bu dosyayi destek ekibine gonderin.' + #13#10 +
        'Inno Setup native log: %TEMP%\Setup Log * .txt',
        mbError, MB_OK);
    end
    else
    begin
      MsgBox(
        'Kurulum basariyla tamamlandi.' + #13#10 + #13#10 +
        'Detayli kurulum log dosyasi (sorun cikarsa paylasin):' + #13#10 +
        InstallLogPath,
        mbInformation, MB_OK);
    end;
  end;
end;
