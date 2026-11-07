@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "RDR2_DIR=%~1"
if "%RDR2_DIR%"=="" set "RDR2_DIR=D:\Red Dead Redemption 2"

if not exist "%RDR2_DIR%\RDR2.exe" (
    echo [ERROR] No se encontro RDR2.exe en:
    echo         %RDR2_DIR%
    echo Uso: Install.bat "D:\ruta\Red Dead Redemption 2"
    exit /b 1
)

if not exist "%RDR2_DIR%\ScriptHookRDRNetAPI.dll" (
    echo [ERROR] Falta ScriptHookRDRNetAPI.dll en:
    echo         %RDR2_DIR%
    echo Instala ScriptHookRDR2DotNet V2.2.0 antes de compilar.
    exit /b 1
)

set "MSBUILD="
where msbuild >nul 2>nul
if not errorlevel 1 set "MSBUILD=msbuild"

if "%MSBUILD%"=="" (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if exist "%VSWHERE%" (
        for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
            set "MSBUILD=%%i"
        )
    )
)

if "%MSBUILD%"=="" (
    echo [ERROR] No se encontro MSBuild. Instala Visual Studio 2022 con la carga de trabajo .NET.
    exit /b 1
)

echo [1/3] Compilando TrucoRDR2...
"%MSBUILD%" "%SCRIPT_DIR%TrucoRDR2.csproj" /m /p:Configuration=Release /p:Platform=x64 /p:RDR2Dir="%RDR2_DIR%"
if errorlevel 1 (
    echo [ERROR] Fallo la compilacion. Revisa la salida anterior.
    exit /b 1
)

if not exist "%RDR2_DIR%\scripts" mkdir "%RDR2_DIR%\scripts"
if errorlevel 1 exit /b 1

echo [2/3] Instalando DLL e INI...
copy /y "%SCRIPT_DIR%bin\Release\TrucoRDR2.dll" "%RDR2_DIR%\scripts\TrucoRDR2.dll" >nul
if errorlevel 1 exit /b 1
copy /y "%SCRIPT_DIR%TrucoRDR2.ini" "%RDR2_DIR%\scripts\TrucoRDR2.ini" >nul
if errorlevel 1 exit /b 1

echo [3/3] Listo.
echo La partida guardada se creara en scripts\TrucoRDR2.save al finalizar cada mano.
echo El YTD debe estar en lml\stream\trucor2_cards.ytd e incluir reverso.
echo Inicia RDR2 en modo Historia y abre la mesa con Ctrl+F9.
exit /b 0
