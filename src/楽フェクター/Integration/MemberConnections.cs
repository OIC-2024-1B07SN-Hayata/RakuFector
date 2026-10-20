using CommonAudio;
namespace FXForge.Integration;
public static class MemberConnections
{
    public static void Configure(MainWindow window, CommonAudioInput audio)
    {
        audio.ProcessOutput = window.ToneView.ProcessAudioOutput;
        audio.ResetOutput = window.ToneView.ResetAudio;
        audio.AudioReceived += window.ToneView.ReceiveAudio;
        audio.AudioReceived += (samples, sampleRate, channels) =>
            window.TunerView.ReceiveAudio(samples, sampleRate, audio.InputChannel);
    }
}
