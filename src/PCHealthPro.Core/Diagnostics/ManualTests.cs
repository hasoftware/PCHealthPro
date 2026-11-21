namespace PCHealthPro.Core.Diagnostics;

public enum ManualTest { Camera, Microphone, Speakers, Display, Keyboard, Pointer }
public enum TestVerdict { Untested, Pass, Fail, Skip }
public sealed record ManualTestResult(ManualTest Test, TestVerdict Verdict);

public sealed class ManualTestSession
{
    private readonly Dictionary<ManualTest, TestVerdict> results = [];
    public TestVerdict Get(ManualTest test) => results.GetValueOrDefault(test, TestVerdict.Untested);
    public void Mark(ManualTest test, TestVerdict verdict) => results[test] = verdict;
    public ManualTestResult[] Snapshot() => Enum.GetValues<ManualTest>().Select(t => new ManualTestResult(t, Get(t))).ToArray();
}
