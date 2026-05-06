# MediaScribe Windows

MediaScribe Windows is a lightweight portable recorder and local transcription tool for Windows 10/11.

It records your microphone plus optional Windows audio, either from the full desktop output or from one selected application/window such as Teams, Chrome, Edge, or another process. Recordings are saved locally as `.wav`, then transcribed locally with `whisper.cpp` using the included `small` or `medium` model.

No cloud transcription API is used.

## Features

- Portable Windows app, no installer required.
- Record microphone only, microphone plus full desktop audio, or microphone plus one application/process.
- Application capture uses Windows process loopback audio.
- Smooth audio meters for microphone and Windows/application audio.
- Adjustable gain for microphone and Windows/application sources.
- Local transcription with `whisper.cpp`.
- Model selector: `small` or `medium`.
- Language selector, French by default.
- Import and transcribe existing audio/video files.
- Transcript panel with copy and open `.txt` actions.
- Local folders next to the app:
  - `Recordings`
  - `Logs`
  - `Settings`
  - `Tools`
  - `Models`

## Download

For normal use, download the latest release from:

https://github.com/Vayaris/MediaScribe-Windows/releases

Recommended asset:

- `MediaScribeRecorder-Portable-UI-Fix.zip` or newer portable ZIP

The release may also include `MediaScribeRecorder.exe` as a direct app executable, but the full ZIP is recommended because transcription needs the `Tools` and `Models` folders beside the app.

## How To Use

1. Extract the portable ZIP.
2. Run `MediaScribeRecorder.exe`.
3. Choose whether to record Windows/application audio:
   - uncheck `Enregistrer Windows / application` for microphone only
   - choose `Tout le bureau` for full desktop sound
   - choose `Application` and select a window/process for app-only audio
4. Choose your microphone.
5. Click `Enregistrer`.
6. Click `Stop`.
7. The recording is saved as `.wav` and transcription starts automatically.
8. Copy the transcript or open the generated `.txt`.

To transcribe an existing file, use `Importer et transcrire`.

Supported import formats include:

```text
.wav .mp3 .mp4 .m4a .aac .flac .ogg .webm .mkv .mov .avi
```

## Settings

Open `Paramètres` to adjust:

- Windows/application gain.
- Microphone gain.
- Whisper model:
  - `small`: faster and lighter
  - `medium`: more accurate but slower and heavier

## Portable Layout

```text
MediaScribeRecorder.exe
Recordings/
Logs/
Settings/
Tools/
  MediaScribeProcessLoopback.exe
  whisper-cli.exe
  ffmpeg.exe
  ffprobe.exe
  whisper.dll
  ggml*.dll
Models/
  ggml-small.bin
  ggml-medium.bin
```

## Build From Source

Requirements:

- Windows 10/11 x64.
- .NET 8 SDK.
- Visual Studio 2022 Build Tools with C++ tools.
- FFmpeg available in `PATH` if `Tools\ffmpeg.exe` and `Tools\ffprobe.exe` are not already present.

Build:

```powershell
.\Build-Portable.ps1
```

The script:

1. Builds the native process-loopback helper.
2. Downloads `whisper.cpp` Windows x64 binaries if needed.
3. Copies `ffmpeg.exe` and `ffprobe.exe`.
4. Downloads `ggml-small.bin` and `ggml-medium.bin` if needed.
5. Publishes the self-contained Windows app.

Output:

```text
bin\Release\net8.0-windows10.0.20348.0\win-x64\publish
```

## Relationship To MediaScribe

The original MediaScribe project is a self-hosted Ubuntu/Debian web app for local transcription.

MediaScribe Windows is a separate portable Windows desktop tool focused on:

- capturing desktop/application audio plus microphone
- saving a local audio file
- transcribing locally with the same `whisper.cpp` approach

## Privacy

Everything runs locally on your computer. Audio and transcripts are not sent to external transcription APIs.

## License Notes

This project uses:

- `whisper.cpp` from ggml-org
- FFmpeg
- NAudio
- Windows process loopback capture code based on public Windows audio APIs and bundled source under its included license

Review each dependency license before redistributing modified builds.
