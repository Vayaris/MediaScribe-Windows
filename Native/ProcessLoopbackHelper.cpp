#include <Windows.h>
#include <combaseapi.h>
#include <comdef.h>
#include <filesystem>
#include <fcntl.h>
#include <iostream>
#include <io.h>
#include <string>
#include <thread>
#include <vector>

#include "ProcessLoopbackCapture.h"

namespace
{
    constexpr unsigned int SampleRate = 48000;
    constexpr unsigned int BitDepth = 16;
    constexpr unsigned int Channels = 2;

    CRITICAL_SECTION g_fileLock;
    HANDLE g_file = INVALID_HANDLE_VALUE;
    DWORD g_dataSize = 0;
    WAVEFORMATEX g_format{};
    bool g_rawStdout = false;

    void WriteHeader()
    {
        DWORD written = 0;
        DWORD riff[] = {
            1179011410,
            0,
            1163280727,
            544501094,
            sizeof(WAVEFORMATEX)
        };
        WriteFile(g_file, riff, sizeof(riff), &written, nullptr);
        WriteFile(g_file, &g_format, sizeof(WAVEFORMATEX), &written, nullptr);

        DWORD data[] = { 1635017060, 0 };
        WriteFile(g_file, data, sizeof(data), &written, nullptr);
    }

    void FixHeader()
    {
        DWORD written = 0;
        SetFilePointer(g_file, 20 + sizeof(WAVEFORMATEX) + sizeof(DWORD), nullptr, FILE_BEGIN);
        WriteFile(g_file, &g_dataSize, sizeof(DWORD), &written, nullptr);

        DWORD totalSize = g_dataSize + 20 + sizeof(WAVEFORMATEX) + 8 - 8;
        SetFilePointer(g_file, sizeof(DWORD), nullptr, FILE_BEGIN);
        WriteFile(g_file, &totalSize, sizeof(DWORD), &written, nullptr);
        FlushFileBuffers(g_file);
    }

    void OnData(const std::vector<unsigned char>::iterator& first, const std::vector<unsigned char>::iterator& last, void*)
    {
        EnterCriticalSection(&g_fileLock);
        if (g_file != INVALID_HANDLE_VALUE && first != last)
        {
            DWORD written = 0;
            const auto bytes = static_cast<DWORD>(std::distance(first, last));
            WriteFile(g_file, &(*first), bytes, &written, nullptr);
            g_dataSize += written;
        }
        LeaveCriticalSection(&g_fileLock);
    }

    bool StopRequested(const std::wstring& stopFile)
    {
        return GetFileAttributesW(stopFile.c_str()) != INVALID_FILE_ATTRIBUTES;
    }
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc < 4)
    {
        std::wcerr << L"Usage: MediaScribeProcessLoopback.exe <pid> <output.wav|-> <stop-file>\n";
        return 2;
    }

    const DWORD processId = wcstoul(argv[1], nullptr, 10);
    const std::wstring outputFile = argv[2];
    const std::wstring stopFile = argv[3];
    if (processId == 0 || outputFile.empty() || stopFile.empty())
    {
        std::wcerr << L"Invalid arguments.\n";
        return 2;
    }

    const HRESULT co = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(co))
    {
        std::wcerr << L"CoInitializeEx failed: 0x" << std::hex << co << L"\n";
        return 3;
    }

    InitializeCriticalSection(&g_fileLock);
    ProcessLoopbackCapture capture;

    auto cleanup = [&]()
    {
        capture.StopCapture();
        EnterCriticalSection(&g_fileLock);
        if (g_file != INVALID_HANDLE_VALUE)
        {
            if (!g_rawStdout)
            {
                FixHeader();
                CloseHandle(g_file);
            }
            g_file = INVALID_HANDLE_VALUE;
        }
        LeaveCriticalSection(&g_fileLock);
        DeleteCriticalSection(&g_fileLock);
        CoUninitialize();
    };

    auto error = capture.SetCaptureFormat(SampleRate, BitDepth, Channels, WAVE_FORMAT_PCM);
    if (error == eCaptureError::NONE)
    {
        error = capture.SetTargetProcess(processId, true);
    }
    if (error == eCaptureError::NONE)
    {
        error = capture.SetCallback(&OnData);
    }

    g_format.wFormatTag = WAVE_FORMAT_PCM;
    g_format.nChannels = Channels;
    g_format.nSamplesPerSec = SampleRate;
    g_format.wBitsPerSample = BitDepth;
    g_format.nBlockAlign = g_format.nChannels * g_format.wBitsPerSample / 8;
    g_format.nAvgBytesPerSec = g_format.nSamplesPerSec * g_format.nBlockAlign;

    if (outputFile == L"-")
    {
        _setmode(_fileno(stdout), _O_BINARY);
        g_rawStdout = true;
        g_file = GetStdHandle(STD_OUTPUT_HANDLE);
    }
    else
    {
        g_file = CreateFileW(outputFile.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    }

    if (g_file == INVALID_HANDLE_VALUE || g_file == nullptr)
    {
        std::wcerr << L"Could not create output file.\n";
        cleanup();
        return 4;
    }
    if (!g_rawStdout)
    {
        WriteHeader();
    }

    if (error == eCaptureError::NONE)
    {
        error = capture.StartCapture();
    }
    if (error != eCaptureError::NONE)
    {
        const HRESULT hr = capture.GetLastErrorResult();
        std::wcerr << L"StartCapture failed: " << static_cast<int>(error) << L" / 0x" << std::hex << hr
                   << L" / " << _com_error(hr).ErrorMessage() << L"\n";
        cleanup();
        return 5;
    }

    while (!StopRequested(stopFile))
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    cleanup();
    return 0;
}
