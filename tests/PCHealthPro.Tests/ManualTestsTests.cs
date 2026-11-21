using System.Buffers.Binary;
using PCHealthPro.Core.Diagnostics;
using Xunit;

namespace PCHealthPro.Tests;

public class ManualTestsTests
{
    [Fact]
    public void SnapshotPreservesIndependentVerdictsAndEarlierSnapshot()
    {
        var session = new ManualTestSession();
        session.Mark(ManualTest.Microphone, TestVerdict.Pass);
        session.Mark(ManualTest.Speakers, TestVerdict.Fail);
        session.Mark(ManualTest.Pointer, TestVerdict.Skip);
        var report = session.Snapshot();
        session.Mark(ManualTest.Microphone, TestVerdict.Fail);
        Assert.Equal(6, report.Length);
        Assert.Contains(new ManualTestResult(ManualTest.Microphone, TestVerdict.Pass), report);
        Assert.Contains(new ManualTestResult(ManualTest.Speakers, TestVerdict.Fail), report);
        Assert.Contains(new ManualTestResult(ManualTest.Pointer, TestVerdict.Skip), report);
        Assert.Equal(TestVerdict.Untested, session.Get(ManualTest.Camera));
        Assert.Equal(TestVerdict.Fail, session.Get(ManualTest.Microphone));
    }

    [Theory]
    [InlineData(SpeakerChannel.Left, false)]
    [InlineData(SpeakerChannel.Right, false)]
    [InlineData(SpeakerChannel.Stereo, true)]
    public void ToneRoutesPcmToRequestedChannels(SpeakerChannel channel, bool sweep)
    {
        var pcm = TestAudio.Tone(channel, sweep);
        Assert.Equal(TestAudio.SampleRate * 4 * (sweep ? 3 : 1), pcm.Length);
        int maxLeft = 0, maxRight = 0, earlyCrossings = 0, lateCrossings = 0;
        int previous = 0;
        for (int offset = 0; offset < pcm.Length; offset += 4)
        {
            int left = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset));
            int right = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset + 2));
            maxLeft = Math.Max(maxLeft, Math.Abs(left));
            maxRight = Math.Max(maxRight, Math.Abs(right));
            if (sweep && previous < 0 && left >= 0)
            {
                if (offset < pcm.Length / 3) earlyCrossings++;
                if (offset >= pcm.Length * 2 / 3) lateCrossings++;
            }
            previous = left;
        }
        Assert.Equal(channel != SpeakerChannel.Right, maxLeft > 0);
        Assert.Equal(channel != SpeakerChannel.Left, maxRight > 0);
        if (sweep) Assert.True(lateCrossings > 5 * earlyCrossings);
    }

    [Fact]
    public void MicMeterHandlesClippingAndSilence()
    {
        Assert.Equal(1d, TestAudio.Peak([0, 128, 0, 0]));
        Assert.Equal(0d, TestAudio.Peak([0, 0]));
        Assert.Equal(0.5d, TestAudio.Peak([0, 64]));
        Assert.Equal(480_000, TestAudio.RecordingBytes);
    }

    [Fact]
    public void RecordingTakesExactlyFiveSecondsAcrossUnevenBuffers()
    {
        var capture = new MicrophoneClip();
        var reused = Enumerable.Repeat((byte)21, TestAudio.RecordingBytes - 6).ToArray();
        Assert.Null(capture.Append(reused));
        Array.Fill(reused, (byte)99);
        var clip = capture.Append(reused);
        Assert.NotNull(clip);
        Assert.Equal(TestAudio.RecordingBytes, clip.Length);
        Assert.Equal(21, clip[^7]);
        Assert.Equal(99, clip[^6]);
        Assert.Null(capture.Append(reused));
    }
}
