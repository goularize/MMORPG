@echo off
echo =======================================
echo Setting up Unity Client Symlinks (Windows)
echo =======================================

if not exist "Client\Assets\Scripts\Shared" (
    mkdir "Client\Assets\Scripts\Shared"
)

echo Creating Network Junction...
mklink /J "Client\Assets\Scripts\Shared\Network" "Shared\Network"

echo Creating Math Junction...
mklink /J "Client\Assets\Scripts\Shared\Math" "Shared\Math"

echo.
echo Setup Complete! You can now open the Client folder in Unity.
pause
