using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace CommonAudio;

public sealed record PcAudioDevice(string Id, string Name)
{
    public override string ToString() => Name;

    public static PcAudioDevice[] GetDevices()
    {
        var result = new List<PcAudioDevice>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) result.Add(new PcAudioDevice(device.ID, device.FriendlyName));
        }
        return result.ToArray();
    }
}
