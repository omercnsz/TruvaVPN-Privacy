@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo   TRUVA VPN - Inno Setup Installer Olusturma Araci
echo ========================================================
echo.

cd /d "%~dp0.."

:: 1. Dahili Build (DLL'ler olusturulsun)
echo [1/5] Proje derleniyor (dotnet publish)...
dotnet publish TruvaDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish > Setup\publish_log.txt 2>&1

:: [ATLANDI] Karmaşıklaştırma (Obfuscar) - Framework yolları uyumsuzluğu nedeniyle geçici olarak devre dışı.
:: Kod içi korumalar (Anti-Debug, URL Masking) aktif kalmaya devam edecektir.

:: 5. Bagimliliklari Kopyala
echo [5/5] Bagimliliklar (wintun.dll, sing-box.exe) kopyalaniyor...
copy /Y sing-box.exe publish\ >nul
copy /Y wintun.dll publish\ >nul

:: 3. ISCC.exe (Inno Setup Compiler) Bul
echo [3/4] Inno Setup Derleyicisi (ISCC.exe) araniyor...

set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "!ISCC!" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"

if not exist "!ISCC!" (
    echo [HATA] ISCC.exe bulunamadi!
    echo Lutfen Inno Setup 6'nin kurulu oldugundan emin olun.
    echo Varsa, yolun dogru oldugunu kontrol edin: !ISCC!
    pause
    exit /b 1
)

:: 4. Installer'i Derle
echo [4/4] Kurulum dosyasi (.exe) olusturuluyor...
"!ISCC!" Setup\truva_setup.iss

if %errorlevel% neq 0 (
    echo [HATA] Kurulum dosyasi olusturulamadi!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo   ISLEM TAMAMLANDI!
echo   Kurulum dosyasi: Setup\Output\TruvaVPN_Setup_v1.0.5.3.exe
echo ========================================================
echo.
pause
