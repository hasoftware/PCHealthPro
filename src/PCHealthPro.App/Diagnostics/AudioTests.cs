using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PCHealthPro.Core.Diagnostics;

namespace PCHealthPro.App.Diagnostics;

internal sealed class AudioTests : IDisposable
{
    private sealed record Endpoint(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly TestControls ui;
    private MMDeviceEnumerator? enumerator;
    private readonly ComboBox inputs, outputs;
    private readonly Button monitor, record, replay, micStop, speakerStop;
    private readonly List<Button> tones = [];
    private readonly ProgressBar level = new() { Maximum = 1 };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Value = 20, StepFrequency = 1 };
    private readonly TextBlock micStatus, speakerStatus;
    private readonly DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly WaveFormat mono = new(TestAudio.SampleRate, 16, 1);
    private WasapiCapture? capture;
    private WasapiOut? player;
    private MMDevice? input, output;
    private RawSourceWaveStream? stream;
    private VolumeSampleProvider? gain;
    private byte[]? clip;
    private bool recording;
    private bool closed, playingMic;

    public AudioTests(Panel host, TestControls ui)
    {
        this.ui = ui;
        ui.Card(host, ManualTest.Microphone, out var mic);
        inputs = ui.Picker(mic, "MicDevice");
        var levelLabel = ui.Text(ui.T("MicLevel"));
        levelLabel.Style = (Style)Application.Current.Resources["MetricLabel"];
        mic.Children.Add(levelLabel);
        mic.Children.Add(level);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(level, ui.T("MicLevel"));
        var micActions = ui.Actions(mic);
        monitor = ui.Button(micActions, "MicMonitor", (_, _) => Capture(false), primary: true);
        record = ui.Button(micActions, "MicRecord", (_, _) => Capture(true));
        replay = ui.Button(micActions, "MicReplay", (_, _) => { if (clip != null) Play(clip, mono, true); });
        micStop = ui.Button(micActions, "TestStop", (_, _) => { StopMic(); if (playingMic) StopPlayer(); micStatus!.Text = ""; Refresh(); });
        micStatus = ui.Text();
        mic.Children.Add(micStatus);

        ui.Card(host, ManualTest.Speakers, out var speaker);
        outputs = ui.Picker(speaker, "SpeakerDevice");
        volume.Header = ui.T("SpeakerVolume");
        speaker.Children.Add(volume);
        volume.ValueChanged += (_, _) => { if (gain != null) gain.Volume = (float)(volume.Value / 100); };
        var speakerActions = ui.Actions(speaker);
        foreach (var channel in Enum.GetValues<SpeakerChannel>())
            tones.Add(ui.Button(speakerActions, "Tone" + channel, (_, _) => Tone(channel, false)));
        tones.Add(ui.Button(speakerActions, "ToneSweep", (_, _) => Tone(SpeakerChannel.Stereo, true)));
        speakerStop = ui.Button(speakerActions, "TestStop", (_, _) => { StopPlayer(); speakerStatus!.Text = ""; Refresh(); });
        speakerStatus = ui.Text();
        speaker.Children.Add(speakerStatus);
        inputs.SelectionChanged += (_, _) => { StopMic(); if (playingMic) StopPlayer(); clip = null; micStatus.Text = ""; Refresh(); };
        outputs.SelectionChanged += (_, _) => { StopPlayer(); speakerStatus.Text = ""; Refresh(); };
        timeout.Tick += (_, _) => { StopMic(); micStatus.Text = ui.T("MicTimeout"); Refresh(); };
        Load(inputs, DataFlow.Capture, micStatus);
        Load(outputs, DataFlow.Render, speakerStatus);
        if (!inputs.IsEnabled) inputs.Visibility = micActions.Visibility = level.Visibility = levelLabel.Visibility = Visibility.Collapsed;
        if (!outputs.IsEnabled) outputs.Visibility = speakerActions.Visibility = volume.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void Load(ComboBox list, DataFlow flow, TextBlock status)
    {
        try
        {
            var endpoints = new List<Endpoint>();
            enumerator ??= new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (endpoint) endpoints.Add(new(endpoint.ID, endpoint.FriendlyName));
            }
            list.ItemsSource = endpoints;
            list.IsEnabled = endpoints.Count > 0;
            if (endpoints.Count == 0) { status.Text = ui.T(flow == DataFlow.Capture ? "MicAbsent" : "SpeakersAbsent"); return; }
            list.SelectedIndex = 0;
            using var preferred = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            list.SelectedItem = endpoints.FirstOrDefault(e => e.Id == preferred.ID) ?? endpoints[0];
        }
        catch (Exception ex) { status.Text = ui.Error(ex); }
    }

    private void Capture(bool save)
    {
        if (inputs.SelectedItem is not Endpoint ep) return;
        StopMic();
        StopPlayer();
        try
        {
            input = enumerator!.GetDevice(ep.Id);
            var device = new WasapiCapture(input);
            capture = device;
            device.WaveFormat = mono;
            recording = save;
            var take = save ? new MicrophoneClip() : null;
            int meterPending = 0;
            device.DataAvailable += (_, data) =>
            {
                // Consume the WASAPI buffer on the capture thread. Keep at
                // most one meter update pending, even when the UI is busy.
                var block = data.Buffer.AsSpan(0, data.BytesRecorded);
                var finished = take?.Append(block);
                if (Interlocked.Exchange(ref meterPending, 1) != 0 && finished == null) return;
                double peak = TestAudio.Peak(block);
                level.DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref meterPending, 0);
                    if (capture != device || closed) return;
                    level.Value = peak;
                    if (finished == null) return;
                    clip = finished;
                    StopMic();
                    micStatus.Text = ui.T("MicReady");
                    if (outputs.SelectedItem != null) Play(clip, mono, true);
                    Refresh();
                });
            };
            device.RecordingStopped += (_, args) => level.DispatcherQueue.TryEnqueue(() =>
            {
                if (capture != device) return;
                StopMic();
                micStatus.Text = args.Exception == null ? ui.T("TestUnavailable") : ui.Error(args.Exception);
                Refresh();
            });
            device.StartRecording();
            micStatus.Text = ui.T(save ? "MicRecording" : "MicLevel");
            if (save) timeout.Start();
        }
        catch (Exception ex) { StopMic(); micStatus.Text = ui.Error(ex); }
        Refresh();
    }

    private void Tone(SpeakerChannel channel, bool sweep) =>
        Play(TestAudio.Tone(channel, sweep), new WaveFormat(TestAudio.SampleRate, 16, 2), false);

    private void Play(byte[] bytes, WaveFormat format, bool fromMic)
    {
        if (outputs.SelectedItem is not Endpoint ep) return;
        StopPlayer();
        var status = fromMic ? micStatus : speakerStatus;
        try
        {
            output = enumerator!.GetDevice(ep.Id);
            stream = new RawSourceWaveStream(new MemoryStream(bytes, false), format);
            gain = new VolumeSampleProvider(stream.ToSampleProvider()) { Volume = (float)(volume.Value / 100) };
            var device = new WasapiOut(output, AudioClientShareMode.Shared, true, 100);
            player = device;
            playingMic = fromMic;
            device.Init(gain.ToWaveProvider());
            device.PlaybackStopped += (_, args) => level.DispatcherQueue.TryEnqueue(() =>
            {
                if (player != device) return;
                StopPlayer();
                status.Text = args.Exception == null ? ui.T("TestPlaybackDone") : ui.Error(args.Exception);
                Refresh();
            });
            device.Play();
            status.Text = ui.T("TestPlaying");
        }
        catch (Exception ex) { StopPlayer(); status.Text = ui.Error(ex); }
        Refresh();
    }

    private void StopMic()
    {
        timeout.Stop();
        var old = capture;
        capture = null;
        old?.Dispose();
        input?.Dispose();
        input = null;
        recording = false;
        level.Value = 0;
    }

    private void StopPlayer()
    {
        var old = player;
        player = null;
        old?.Dispose();
        stream?.Dispose();
        output?.Dispose();
        output = null;
        stream = null;
        gain = null;
        if (playingMic) micStatus.Text = "";
        else speakerStatus.Text = "";
        playingMic = false;
    }

    private void Refresh()
    {
        monitor.IsEnabled = inputs.SelectedItem != null && capture == null;
        record.IsEnabled = inputs.SelectedItem != null && !recording;
        replay.IsEnabled = clip != null && outputs.SelectedItem != null && capture == null;
        micStop.IsEnabled = capture != null || playingMic;
        foreach (var b in tones) b.IsEnabled = outputs.SelectedItem != null && !recording;
        speakerStop.IsEnabled = player != null && !playingMic;
    }

    public void Dispose()
    {
        closed = true;
        StopMic();
        StopPlayer();
        clip = null;
        enumerator?.Dispose();
    }
}
