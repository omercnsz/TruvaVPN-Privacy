@echo off
echo Building Truva Desktop (Stand-alone EXE)...
dotnet publish TruvaDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish > build_output.log 2>&1

if %errorlevel% neq 0 (
    echo.
    echo [HATA] Derleme basarisiz oldu. Detaylar icin build_output.log dosyasına bakin.
    pause
    exit /b %errorlevel%
)

echo.
echo [BILGI] sing-box.exe, wintun.dll ve WinDivert dosyalari kopyalaniyor...
copy /Y sing-box.exe publish\
copy /Y wintun.dll publish\
copy /Y WinDivert.dll publish\
copy /Y WinDivert64.sys publish\

echo.
echo ===========================================
echo ISLEM TAMAMLANDI!
echo EXE Konumu: TruvaDesktop\publish\TruvaDesktop.exe
echo ===========================================
echo.
pause
