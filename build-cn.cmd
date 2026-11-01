@echo off
REM ============================================================
REM  国服（XIVLauncherCN）编译脚本
REM
REM  子模块 ECommons / ECommons.IPC / NightmareUI 的 csproj 把
REM  DalamudLibPath 写死成 %APPDATA%\XIVLauncher\Addon\Hooks\dev\（国际服启动器）。
REM  国服机器上没有这个目录，直接 dotnet build 会报上千个 CS0246
REM  （Dalamud.Game / FFXIVClientStructs / TerraFX 找不到）。
REM
REM  这里用全局属性 -p:DalamudLibPath 改指国服，会覆盖所有子项目，
REM  不需要改子模块里的文件。注意路径结尾必须用正斜线，
REM  否则 cmd 会把结尾的 \" 当成转义引号，参数就废了。
REM ============================================================
setlocal
set "CN_LIB=%APPDATA%\XIVLauncherCN\addon\Hooks\dev/"

if not exist "%CN_LIB%Dalamud.dll" (
  echo [x] 找不到国服 Dalamud: %CN_LIB%
  echo     先用 XIVLauncherCN 的 dev 模式启动一次游戏，让 Dalamud 把 libs 下到该目录。
  exit /b 1
)

echo [*] DalamudLibPath = %CN_LIB%
dotnet build "%~dp0AutoDuty/AutoDuty.csproj" -p:DalamudLibPath="%CN_LIB%" %*
if errorlevel 1 (
  echo [x] 编译失败
  exit /b 1
)
echo [ok] 编译完成，产物在 %~dp0AutoDuty\bin\ 下
endlocal
