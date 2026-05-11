param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "MediaScribeRecorder.csproj"
$nativeBuild = Join-Path $PSScriptRoot "Native\Build-ProcessLoopback.ps1"
$dependencies = Join-Path $PSScriptRoot "Ensure-PortableDependencies.ps1"

& $nativeBuild
& $dependencies

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

$publishDir = Join-Path $PSScriptRoot "bin\$Configuration\net8.0-windows10.0.20348.0\win-x64\publish"
$publishTools = Join-Path $publishDir "Tools"
$publishModels = Join-Path $publishDir "Models"
New-Item -ItemType Directory -Force -Path $publishTools | Out-Null
New-Item -ItemType Directory -Force -Path $publishModels | Out-Null
foreach ($userDataDirName in @("Recordings", "Logs", "Settings")) {
    $userDataDir = Join-Path $publishDir $userDataDirName
    if (Test-Path $userDataDir) {
        Get-ChildItem -LiteralPath $userDataDir -Force | Remove-Item -Recurse -Force
    }
}

$requiredTools = @(
    "MediaScribeProcessLoopback.exe",
    "whisper-cli.exe",
    "whisper.dll",
    "ggml.dll",
    "ggml-base.dll",
    "ggml-cpu.dll",
    "ffmpeg.exe",
    "ffprobe.exe"
)

foreach ($tool in $requiredTools) {
    $source = Join-Path $PSScriptRoot "Tools\$tool"
    if (-not (Test-Path $source)) {
        throw "Missing portable tool: $source"
    }
    Copy-Item -LiteralPath $source -Destination $publishTools -Force
}

$models = @("ggml-small.bin", "ggml-medium.bin")
foreach ($modelName in $models) {
    $model = Join-Path $PSScriptRoot "Models\$modelName"
    if (-not (Test-Path $model)) {
        throw "Missing Whisper model: $model"
    }
    Copy-Item -LiteralPath $model -Destination $publishModels -Force
}

Write-Host "Portable build ready: $publishDir"
