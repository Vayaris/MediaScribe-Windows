$ErrorActionPreference = "Stop"

$vsDevCmd = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
$nativeRoot = $PSScriptRoot
$srcRoot = Join-Path $nativeRoot "ProcessLoopbackCapture-src"
$outDir = Join-Path (Split-Path $nativeRoot -Parent) "Tools"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$helper = Join-Path $nativeRoot "ProcessLoopbackHelper.cpp"
$capture = Join-Path $srcRoot "ProcessLoopbackCapture.cpp"
$exe = Join-Path $outDir "MediaScribeProcessLoopback.exe"

$cmd = "`"$vsDevCmd`" -arch=x64 -host_arch=x64 && cl /nologo /std:c++20 /EHsc /O2 /DUNICODE /D_UNICODE /I `"$srcRoot`" `"$helper`" `"$capture`" /Fe:`"$exe`" /link mmdevapi.lib avrt.lib ole32.lib"
cmd.exe /c $cmd

Write-Host "Process loopback helper ready: $exe"
