


IF NOT DEFINED KSP2DIR (
    echo Error: KSP2DIR is not defined.
    exit /b 1
)

RMDIR "%KSP2DIR%\BepInEx\plugins\SASed Warp" /S /Q
@setlocal enableextensions
mklink /J "%KSP2DIR%\BepInEx\plugins\SASed Warp" "%~dp0"


