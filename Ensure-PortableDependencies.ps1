$ErrorActionPreference = "Stop"

$toolsDir = Join-Path $PSScriptRoot "Tools"
$modelsDir = Join-Path $PSScriptRoot "Models"
$downloadsDir = Join-Path $PSScriptRoot "Downloads"
New-Item -ItemType Directory -Force -Path $toolsDir, $modelsDir, $downloadsDir | Out-Null

function Download-FileIfMissing {
    param(
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    if (Test-Path $Destination) {
        return
    }

    Write-Host "Downloading $Url"
    Invoke-WebRequest -UseBasicParsing $Url -OutFile $Destination
}

function Copy-FromPathIfMissing {
    param(
        [Parameter(Mandatory=$true)][string]$CommandName,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    if (Test-Path $Destination) {
        return $true
    }

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($command) {
        Copy-Item -LiteralPath $command.Source -Destination $Destination -Force
        return $true
    }

    return $false
}

$release = Invoke-RestMethod -UseBasicParsing "https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest"
$asset = $release.assets | Where-Object name -eq "whisper-bin-x64.zip" | Select-Object -First 1
if (-not $asset) {
    throw "Could not find whisper-bin-x64.zip in the latest whisper.cpp release."
}

$whisperZip = Join-Path $downloadsDir "whisper-bin-x64.zip"
Download-FileIfMissing -Url $asset.browser_download_url -Destination $whisperZip

$whisperExtract = Join-Path $downloadsDir "whisper-bin-x64"
if (-not (Test-Path (Join-Path $toolsDir "whisper-cli.exe"))) {
    if (Test-Path $whisperExtract) {
        Remove-Item -LiteralPath $whisperExtract -Recurse -Force
    }
    Expand-Archive -Path $whisperZip -DestinationPath $whisperExtract
    $releaseDir = Join-Path $whisperExtract "Release"
    foreach ($file in @("whisper-cli.exe", "whisper.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll")) {
        Copy-Item -LiteralPath (Join-Path $releaseDir $file) -Destination $toolsDir -Force
    }
}

if (-not (Copy-FromPathIfMissing -CommandName "ffmpeg" -Destination (Join-Path $toolsDir "ffmpeg.exe"))) {
    throw "ffmpeg.exe was not found in PATH. Install FFmpeg or place ffmpeg.exe in Tools."
}

if (-not (Copy-FromPathIfMissing -CommandName "ffprobe" -Destination (Join-Path $toolsDir "ffprobe.exe"))) {
    throw "ffprobe.exe was not found in PATH. Install FFmpeg or place ffprobe.exe in Tools."
}

Download-FileIfMissing `
    -Url "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin" `
    -Destination (Join-Path $modelsDir "ggml-small.bin")

Download-FileIfMissing `
    -Url "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin" `
    -Destination (Join-Path $modelsDir "ggml-medium.bin")

Write-Host "Portable dependencies are ready."
