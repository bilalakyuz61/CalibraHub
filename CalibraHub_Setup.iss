[Setup]
; Sihirbazın Benzersiz Lisans ve Kimlik Kodu (İlerde lisans şifreleme/güncelleme için bu GUID baz alınacak)
AppId={{9156FC1A-4A82-4116-A1EA-BBE19F1D8B1C}
AppName=CalibraHub E-Fatura Sistemi
AppVersion=1.0.0
AppPublisher=Calibra Software
DefaultDirName={autopf}\CalibraHub
DefaultGroupName=CalibraHub E-Fatura Sistemi
OutputDir=.\install
OutputBaseFilename=CalibraHub_Sistem_Kurulum_v1.0.0
Compression=lzma
SolidCompression=yes
SetupIconFile=compiler:SetupClassicIcon.ico
; Servis (Arka Plan) oluşturmak için Windows'tan kesinlikle Yönetici (Admin) Yetkisi İstesin!
PrivilegesRequired=admin

[Icons]
; Başlat menüsüne uygulamanın Ana dizinini açan bir kısayol
Name: "{group}\CalibraHub Dosyaları"; Filename: "{app}"
Name: "{group}\Sistemi Kurulumdan Kaldır"; Filename: "{uninstallexe}"
; Özel belirlediğimiz PORT ile masaüstü paneli kısayolu
Name: "{commondesktop}\CalibraHub E-Fatura Paneli"; Filename: "http://localhost:{code:GetAppPort}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon} Masaüstüne Panel Kısayolu Oluştur"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Derlenmiş (Publish) edilen tüm güvenli kütüphaneler hedef klasöre alınıyor
Source: "install\Setup_v1.0.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Form ekranından appsettings.json dosyasına veriyi yapıştıracak olan güç kodumuz
Source: "UpdateConfig-Installer.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; 1. Adım: Kullanıcının girdiği SQL bilgilerini ve Port numarasını al, PS1 aracılığıyla JSON'a yapıştır!
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\UpdateConfig-Installer.ps1"" ""{app}\appsettings.json"" ""Server={code:GetDbServer};Database={code:GetDbName};User Id={code:GetDbUser};Password={code:GetDbPass};TrustServerCertificate=True;"" ""{code:GetAppPort}"""; Flags: runhidden waituntilterminated

; 2. Adım: Başarılı Kurulumdan sonra uygulamayı IIS'siz tam otomatik Windows Service (Görev Hizmeti) olarak Arka Planda Başlat!
Filename: "sc.exe"; Parameters: "create CalibraHubService binPath= ""{app}\CalibraHub.Web.exe"" start= auto"; Flags: runhidden
Filename: "sc.exe"; Parameters: "start CalibraHubService"; Flags: runhidden

; 3. Adım: Kurulum bitince "Uygulamayı Başlat" tiki işaretliyse tarayıcıyı web sayfasına aç
Filename: "http://localhost:{code:GetAppPort}"; Description: "CalibraHub Web Panelini Tarayıcıda Aç"; Flags: shellexec postinstall skipifsilent

[UninstallRun]
; Program bilgisayardan (Denetim Masasından) kaldırılırken çalışan servisleri de silsin.
Filename: "sc.exe"; Parameters: "stop CalibraHubService"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete CalibraHubService"; Flags: runhidden waituntilterminated

[Code]
var
  DbPage: TInputQueryWizardPage;
  LicensePage: TInputQueryWizardPage;
  PortPage: TInputQueryWizardPage;

// Kurulum dosyalarından önce "Servisi Durdurma (Güncelleme için)" Operasyonu
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // Dosyalar çıkarma (kopyalama) aşamasından hemen ÖNCE (ssInstall) eğer arkada servis varsa DURDUR!
  if CurStep = ssInstall then
  begin
    Exec('sc.exe', 'stop CalibraHubService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure InitializeWizard;
begin
  // İLERİDE KULLANILACAK OLAN LİSANS SAYFASI KODU ALTYAPISI
  LicensePage := CreateInputQueryPage(wpWelcome,
    'Sistem Lisanslaması', 'Yazılımın aktif edilmesi için gereken lisans numarasını giriniz.',
    'Test veya Satış anahtarınızı lütfen buraya giriniz: (Şimdilik opsiyoneldir)');
  LicensePage.Add('Lisans Anahtarı:', False);

  // KULLANICIYA CIKACAK SQL, VERITABANI ve PORT FORM SAYFASI
  PortPage := CreateInputQueryPage(LicensePage.ID,
    'Web Sunucusu ve Port Yapılandırması', 'Sistem ağda veya kendi cihazınızda hangi Port ile yayın yapacak?',
    'Eğer 5000 veya 80 gibi portlar sisteminizde çakışıyorsa buradan boş olan bir yayın (HTTP) Port numarası (Örn: 5089) giriniz:');
  PortPage.Add('Uygulama Port Numarası:', False);
  PortPage.Values[0] := '5000'; // Varsayılan Panel Portu

  DbPage := CreateInputQueryPage(PortPage.ID,
    'Veritabanı (SQL) Yapılandırması', 'Lütfen sistemin bağlanacağı SQL Server bilgilerini girin.',
    'Program kurulurken appsettings.json içerisine şifreli/güvenli olarak bu bilgiler yazılacaktır.');

  DbPage.Add('SQL Sunucu Adı (Örn: .\SQLEXPRESS veya Local IP):', False);
  DbPage.Add('Veritabanı Adı:', False);
  DbPage.Add('SQL Kullanıcı Adı (Örn: sa):', False);
  DbPage.Add('SQL Şifresi:', True);

  // Örnek Varsayılan(Default) Değerler
  DbPage.Values[0] := '.\SQLEXPRESS';
  DbPage.Values[1] := 'CalibraHubDb';
  DbPage.Values[2] := 'sa';
  DbPage.Values[3] := '';
end;

function GetAppPort(Param: String): String;
begin
  Result := PortPage.Values[0];
end;

function GetDbServer(Param: String): String;
begin
  Result := DbPage.Values[0];
end;

function GetDbName(Param: String): String;
begin
  Result := DbPage.Values[1];
end;

function GetDbUser(Param: String): String;
begin
  Result := DbPage.Values[2];
end;

function GetDbPass(Param: String): String;
begin
  Result := DbPage.Values[3];
end;
