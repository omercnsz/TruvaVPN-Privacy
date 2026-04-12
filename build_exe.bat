@echo off
echo =============================================
echo   TRUVA VPN - EXE Olusturma Araci
echo =============================================
echo.

cd /d "%~dp0"

echo [1/3] Proje derleniyor (Release x64)...
dotnet build TruvaDesktop.csproj -c Release -r win-x64 --no-restore
if %errorlevel% neq 0 (
    echo.
    echo [HATA] Derleme basarisiz! Restore deneniyor...
    dotnet restore TruvaDesktop.csproj
    dotnet build TruvaDesktop.csproj -c Release -r win-x64
    if %errorlevel% neq 0 (
        echo [HATA] Derleme basarisiz oldu!
        pause
        exit /b 1
    )
)

echo.
echo [2/3] Dosyalar kontrol ediliyor...
set "OUTDIR=bin\Release\net10.0-windows10.0.19041.0\win-x64"

if not exist "%OUTDIR%\TruvaDesktop.exe" (
    echo [HATA] TruvaDesktop.exe bulunamadi!
    pause
    exit /b 1
)

echo [3/3] Bagimliliklar kopyalaniyor...
copy /Y sing-box.exe "%OUTDIR%\" >nul 2>&1
copy /Y wintun.dll "%OUTDIR%\" >nul 2>&1

echo.
echo =============================================
echo   BASARILI! EXE dosyaniz hazir:
echo   %OUTDIR%\TruvaDesktop.exe
echo =============================================
echo.
echo Klasoru acmak icin bir tusa basin...
pause >nul
explorer "%OUTDIR%"
