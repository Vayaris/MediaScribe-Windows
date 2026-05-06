# Native process loopback module

Windows can capture audio for one process tree through `ActivateAudioInterfaceAsync` with `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`.

The WPF app already exposes the process/window picker and routes application-mode capture through `ProcessLoopbackCaptureSource`. The remaining native module should export a capture stream compatible with that class, using the Microsoft Application Loopback sample as the reference implementation.

Target API:

```c
int MSR_StartProcessLoopbackCapture(
    unsigned int processId,
    void* userState,
    void (__stdcall *onAudio)(void* userState, const float* samples, int frameCount, int channels, int sampleRate),
    void (__stdcall *onError)(void* userState, const wchar_t* message));

void MSR_StopProcessLoopbackCapture(int handle);
```

Reference:

- https://learn.microsoft.com/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
- https://learn.microsoft.com/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params
