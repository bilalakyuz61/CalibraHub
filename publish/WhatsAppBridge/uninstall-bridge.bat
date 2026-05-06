@echo off
REM ============================================================
REM  CalibraHub WhatsApp Bridge — kaldirma scripti
REM  Kullanim: bu dosyaya SAG TIK -> Yonetici olarak calistir
REM ============================================================
setlocal

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo HATA: Admin yetkisi gerekli. Sag tik -^> Yonetici olarak calistir.
    pause
    exit /b 1
)

cd /d "%~dp0"

echo Bridge servisi durduruluyor ve kaldiriliyor...
sc.exe stop CalibraHubWhatsAppBridge >nul 2>&1
timeout /t 2 /nobreak >nul

if exist "uninstall-service.js" (
    node uninstall-service.js
) else (
    sc.exe delete CalibraHubWhatsAppBridge >nul 2>&1
)

echo.
echo Bridge servisi kaldirildi.
echo Klasor (%CD%) ve session-data dosyalari silinmedi — manuel silebilirsin.
echo.
pause
