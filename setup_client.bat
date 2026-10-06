@echo off
REM Links the shared backend contracts into the Unity client (idempotent, safe to re-run).
REM Shared\Models is intentionally NOT linked: it is server-side data code (C# records
REM that Unity's compiler cannot build without extra polyfills). Add it to the list when needed.
setlocal
cd /d "%~dp0"

echo =======================================
echo Setting up Unity Client Junctions (Windows)
echo =======================================

set "TARGET_DIR=Client\Assets\Scripts\Shared"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

for %%F in (Enums Math Network) do (
    if not exist "Shared\%%F" (
        echo ERROR: Shared\%%F does not exist. Run this script from a full clone.
        exit /b 1
    )
    if exist "%TARGET_DIR%\%%F" (
        echo OK: %%F junction already exists.
    ) else (
        echo Creating %%F junction...
        mklink /J "%TARGET_DIR%\%%F" "Shared\%%F" >nul
        if errorlevel 1 (
            echo ERROR: Failed to create the %%F junction.
            exit /b 1
        )
    )
)

echo.
echo Setup Complete! You can now open the Client folder in Unity.
endlocal
