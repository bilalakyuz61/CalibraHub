@echo off
REM ============================================================
REM  CalibraHub WhatsApp Bridge — sonradan kurulum scripti
REM  Kullanim: bu dosyaya SAG TIK -> Yonetici olarak calistir
REM ============================================================
setlocal

REM Admin yetkisi kontrolu
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ====================================================
    echo  HATA: Bu script admin yetkisi gerektirir.
    echo ====================================================
    echo.
    echo Sag tik -> Yonetici olarak calistir
    echo.
    pause
    exit /b 1
)

cd /d "%~dp0"

echo.
echo ====================================================
echo  CalibraHub WhatsApp Bridge Kurulumu
echo ====================================================
echo.
echo Klasor: %CD%
echo.

REM Node.js kontrolu
where node >nul 2>&1
if %errorlevel% neq 0 (
    echo HATA: Node.js bulunamadi.
    echo.
    echo Lutfen Node.js 18+ yukleyin: https://nodejs.org
    echo Yukledikten sonra bu scripti tekrar calistirin.
    echo.
    pause
    exit /b 1
)

for /f "delims=" %%v in ('node --version') do set NODE_VER=%%v
echo Node.js: %NODE_VER%
echo.

REM Eski servisi durdur ve sil (varsa)
echo [1/3] Eski Bridge servisi temizleniyor...
sc.exe stop CalibraHubWhatsAppBridge >nul 2>&1
sc.exe delete CalibraHubWhatsAppBridge >nul 2>&1
timeout /t 2 /nobreak >nul

REM npm install
echo.
echo [2/3] Bagimliliklar yukleniyor (1-3 dk surebilir, Chromium iner)...
echo.
call npm install --production --no-audit --no-fund
if %errorlevel% neq 0 (
    echo.
    echo HATA: npm install basarisiz.
    pause
    exit /b 1
)

REM Service kayit
echo.
echo [3/3] Windows Service olarak kaydediliyor...
echo.
node install-service.js
if %errorlevel% neq 0 (
    echo.
    echo HATA: Service kayit basarisiz.
    pause
    exit /b 1
)

echo.
echo ====================================================
echo   KURULUM TAMAMLANDI
echo ====================================================
echo.
echo Servis adi: CalibraHubWhatsAppBridge
echo Durum kontrolu: Get-Service CalibraHubWhatsAppBridge
echo Bridge URL:  http://localhost:61100
echo.
echo Sonraki adim:
echo  1. CalibraHub uygulamasina giris yap
echo  2. Sirket Ayarlari -^> WhatsApp tab
echo  3. "Web QR (Node Bridge)" sec
echo  4. Bridge URL: http://localhost:61100 girip Kaydet
echo  5. Baglantiyi Test Et -^> "awaiting_qr" gorursun
echo  6. http://localhost:61100/qr endpoint'inden veya
echo     servis log'larindan QR'i al, telefondan tara
echo.
pause
