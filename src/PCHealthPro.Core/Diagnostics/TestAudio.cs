using System.Buffers.Binary;

namespace PCHealthPro.Core.Diagnostics;

public enum SpeakerChannel { Left, Right, Stereo }

public sealed class MicrophoneClip
{
    private readonly byte[] pcm = new byte[TestAudio.RecordingBytes];
    private int used;

    // Called by a single capture thread. Only a complete five-second clip
    // is handed to playback, and subsequent callbacks cannot modify it.
    public byte[]? Append(ReadOnlySpan<byte> block)
    {
        if (used == pcm.Length) return null;
        int count = Math.Min(block.Length, pcm.Length - used);
        block[..count].CopyTo(pcm.AsSpan(used));
        used += count;
        return used == pcm.Length ? pcm : null;
    }
}

public static class TestAudio
{
    public const int SampleRate = 48000;
    public const int RecordingBytes = SampleRate * sizeof(short) * 5;

    public static double Peak(ReadOnlySpan<byte> monoPcm16)
    {
        int max = 0;
        for (int i = 0; i + 1 < monoPcm16.Length; i += 2)
            max = Math.Max(max, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(monoPcm16[i..])));
        return max / 32768d;
    }

    public static byte[] Tone(SpeakerChannel channel, bool sweep)
    {
        int frames = SampleRate * (sweep ? 3 : 1);
        byte[] pcm = new byte[frames * 4];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double hz = sweep ? 80 * Math.Pow(12000d / 80, (double)i / (frames - 1)) : 440;
            double envelope = Math.Clamp(Math.Min(i, frames - 1 - i) / (0.01 * SampleRate), 0, 1);
            short sample = (short)(short.MaxValue * 0.5 * envelope * Math.Sin(phase));
            phase = (phase + 2 * Math.PI * hz / SampleRate) % (2 * Math.PI);
            if (channel != SpeakerChannel.Right) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4), sample);
            if (channel != SpeakerChannel.Left) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4 + 2), sample);
        }
        return pcm;
    }
}
