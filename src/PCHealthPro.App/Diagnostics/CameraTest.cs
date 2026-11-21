using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace PCHealthPro.App.Diagnostics;

internal sealed class CameraTest : IDisposable
{
    private readonly TestControls ui;
    private readonly ComboBox devices;
    private readonly Button start, stop, snap;
    private readonly TestActions actions;
    private readonly Image preview = new() { MaxHeight = 300 };
    private readonly Image photo = new() { MaxHeight = 160 };
    private readonly TextBlock status;
    private MediaCapture? capture;
    private MediaFrameReader? reader;
    private SoftwareBitmapSource? live;
    private bool takePhoto;
    private int generation, frameBusy;
    private bool closed;

    public CameraTest(StackPanel body, TestControls ui)
    {
        this.ui = ui;
        devices = ui.Picker(body, "CameraDevice");
        devices.DisplayMemberPath = "Name";
        actions = ui.Actions(body);
        start = ui.Button(actions, "CameraStart", async (_, _) => await StartAsync(), primary: true);
        snap = ui.Button(actions, "CameraSnap", (_, _) => takePhoto = true);
        stop = ui.Button(actions, "TestStop", (_, _) => Stop());
        status = ui.Text();
        body.Children.Add(status);
        body.Children.Add(preview);
        body.Children.Add(photo);
        devices.SelectionChanged += (_, _) => Stop();
        Stop();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var list = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (closed) return;
            devices.ItemsSource = list.ToArray();
            devices.SelectedIndex = list.Count == 0 ? -1 : 0;
            devices.IsEnabled = list.Count > 0;
            if (list.Count == 0)
            {
                devices.Visibility = actions.Visibility = Visibility.Collapsed;
                status.Text = ui.T("CameraAbsent");
            }
        }
        catch (Exception ex) { if (!closed) status.Text = ui.Error(ex); }
    }

    private async Task StartAsync()
    {
        if (devices.SelectedItem is not DeviceInformation device || closed) return;
        Stop();
        int id = generation;
        start.IsEnabled = false;
        stop.IsEnabled = true;
        status.Text = ui.T("TestOpening");
        // Keep initialization resources local until each await is validated.
        MediaCapture? local = new();
        MediaFrameReader? frames = null;
        try
        {
            await local.InitializeAsync(new MediaCaptureInitializationSettings {
                VideoDeviceId = device.Id, StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu, SharingMode = MediaCaptureSharingMode.SharedReadOnly });
            if (id != generation) return;
            var source = local.FrameSources.Values.Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderByDescending(s => s.Info.MediaStreamType == MediaStreamType.VideoPreview).FirstOrDefault()
                ?? throw new InvalidOperationException(ui.T("NoColorStream"));
            frames = await local.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            if (id != generation) return;
            frames.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            var result = await frames.StartAsync();
            if (id != generation) return;
            if (result != MediaFrameReaderStartStatus.Success) throw new InvalidOperationException(result.ToString());
            capture = local;
            reader = frames;
            local = null;
            frames = null;
            capture.Failed += Failed;
            preview.Source = live = new SoftwareBitmapSource();
            preview.Visibility = Visibility.Visible;
            reader.FrameArrived += Frame;
            var format = source.CurrentFormat;
            var fps = format.FrameRate;
            status.Text = $"{format.VideoFormat.Width} × {format.VideoFormat.Height} · " +
                (fps.Denominator == 0 ? ui.T("Unknown") : $"{(double)fps.Numerator / fps.Denominator:0.##} FPS");
        }
        catch (Exception ex) { if (id == generation) { Stop(); status.Text = ui.Error(ex); } }
        finally { frames?.Dispose(); local?.Dispose(); }
    }

    private void Failed(MediaCapture sender, MediaCaptureFailedEventArgs e) =>
        preview.DispatcherQueue.TryEnqueue(() => { if (capture == sender) { Stop(); status.Text = ui.T("TestUnavailable"); } });

    private void Frame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.Exchange(ref frameBusy, 1) != 0) return;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap == null) { Interlocked.Exchange(ref frameBusy, 0); return; }
            var copy = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            if (preview.DispatcherQueue.TryEnqueue(async () =>
            {
                using (copy)
                try
                {
                    if (reader != sender || live == null) return;
                    await live.SetBitmapAsync(copy);
                    if (reader != sender) return;
                    snap.IsEnabled = true;
                    if (takePhoto)
                    {
                        takePhoto = false;
                        var still = new SoftwareBitmapSource();
                        await still.SetBitmapAsync(copy);
                        if (reader == sender) { photo.Source = still; photo.Visibility = Visibility.Visible; }
                    }
                }
                catch (Exception ex) { if (reader == sender) { Stop(); status.Text = ui.Error(ex); } }
                finally { Interlocked.Exchange(ref frameBusy, 0); }
            })) return;
            copy.Dispose();
        }
        catch (Exception ex)
        {
            preview.DispatcherQueue.TryEnqueue(() => { if (reader == sender) { Stop(); status.Text = ui.Error(ex); } });
        }
        Interlocked.Exchange(ref frameBusy, 0);
    }

    private void Stop()
    {
        generation++;
        var oldReader = reader;
        reader = null;
        if (oldReader != null) { oldReader.FrameArrived -= Frame; oldReader.Dispose(); }
        if (capture != null) { capture.Failed -= Failed; capture.Dispose(); capture = null; }
        preview.Source = photo.Source = null;
        preview.Visibility = photo.Visibility = Visibility.Collapsed;
        takePhoto = false;
        live = null;
        status.Text = "";
        snap.IsEnabled = stop.IsEnabled = false;
        start.IsEnabled = !closed && devices.SelectedItem != null;
    }

    public void Dispose() { closed = true; Stop(); }
}
