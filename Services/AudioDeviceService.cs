using MediaScribeRecorder.Models;
using NAudio.CoreAudioApi;

namespace MediaScribeRecorder.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName))
            .ToList();
    }

    public MMDevice GetCaptureDevice(string? id)
    {
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                return enumerator.GetDevice(id);
            }

            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch (Exception ex)
        {
            throw new UserFacingException("REC-MIC-001", "Micro indisponible. Vérifiez le micro sélectionné dans Windows ou choisissez un autre périphérique.", ex);
        }
    }

    public MMDevice GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
