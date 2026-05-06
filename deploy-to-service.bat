@echo off
REM ─────────────────────────────────────────────────────────────────
REM CalibraHub Web servisi guncelleme — bin/Debug deploy + restart.
REM YONETICI olarak calistirin (sag tik → "Yonetici olarak calistir").
REM
REM Adimlar:
REM   1) sc stop "CalibraHub Web"
REM   2) bin/Debug/net10.0/* → C:\Program Files\CalibraHub\Web\
REM   3) sc start "CalibraHub Web"
REM   4) Yeni binary'lerle servis 61001'de calisir; static (CSS/JS),
REM      menu (Operasyon Tanimlamalari, Sirket Parametreleri),
REM      WorkOrderEdit yeni layout, vs. hepsi aktif olur.
REM
REM Calistirma:
REM   D:\JetBrainsRider\Projeler\CalibraHub\deploy-to-service.bat
REM ─────────────────────────────────────────────────────────────────

setlocal enabledelayedexpansion

set "SOURCE=%~dp0src\CalibraHub.Web\bin\Debug\net10.0"
set "TARGET=C:\Program Files\CalibraHub\Web"
set "SERVICE=CalibraHub Web"

echo.
echo === CalibraHub Web Service Update ===
echo.
echo Kaynak : %SOURCE%
echo Hedef  : %TARGET%
echo Servis : %SERVICE%
echo.

REM Kaynak var mi?
if not exist "%SOURCE%\CalibraHub.Web.dll" (
    echo HATA: Kaynak klasor bulunamadi veya bos: %SOURCE%
    echo Once "dotnet build" calistirin.
    pause
    exit /b 1
)

REM Hedef var mi?
if not exist "%TARGET%" (
    echo HATA: Hedef klasor yok: %TARGET%
    pause
    exit /b 1
)

echo [1/4] Servis durduruluyor...
sc stop "%SERVICE%"
if errorlevel 1 (
    echo  ! Servis zaten durdurulmus olabilir, devam ediliyor.
)

REM Servis durana kadar bekle (max 15 sn)
set /a ATTEMPTS=0
:WAIT_STOP
sc query "%SERVICE%" | find "STOPPED" >nul
if errorlevel 1 (
    set /a ATTEMPTS+=1
    if !ATTEMPTS! geq 15 (
        echo  ! Servis durmadi, yine de devam ediliyor.
        goto AFTER_STOP
    )
    timeout /t 1 /nobreak >nul
    goto WAIT_STOP
)
:AFTER_STOP

echo [2/4] Yeni binary'ler kopyalaniyor...
REM /MIR yerine /E + /XO: hedefte fazla dosya silinmesin (config, log).
robocopy "%SOURCE%" "%TARGET%" /E /XO /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 (
    echo HATA: Kopyalama basarisiz oldu.
    pause
    exit /b 1
)

echo [3/4] Servis baslatiliyor...
sc start "%SERVICE%"
if errorlevel 1 (
    echo HATA: Servis baslatilamadi.
    pause
    exit /b 1
)

echo [4/4] Servis durumu:
timeout /t 2 /nobreak >nul
sc query "%SERVICE%" | findstr "STATE"

echo.
echo === Tamamlandi ===
echo http://localhost:61001/ adresinden test edebilirsiniz.
echo.
endlocal
pause
