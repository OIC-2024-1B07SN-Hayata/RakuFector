using System;

namespace CommonAudio;

public static class AudioLatency
{
    public const int DefaultPcMilliseconds = 10;
    public static int QueueCapacityFrames(int sampleRate, int inputBlockFrames, int pcMilliseconds)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (inputBlockFrames < 0) throw new ArgumentOutOfRangeException(nameof(inputBlockFrames));
        if (pcMilliseconds < 5 || pcMilliseconds > 100) throw new ArgumentOutOfRangeException(nameof(pcMilliseconds));
        // 通常は20msまで。ASIOが大きなブロックを返す場合は1ブロック＋PC要求分を保持。
        return Math.Max(1, Math.Max(sampleRate / 50, inputBlockFrames + sampleRate * pcMilliseconds / 1000));
    }
}
