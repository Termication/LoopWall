using System.Buffers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LoopWall.App.Interop;
using LoopWall.App.Models;

namespace LoopWall.App.Views;

/// <summary>
/// A diagnostic surface for the native decoder. It intentionally maintains only
/// one queued frame; if WPF is busy, newer decoded frames are dropped.
/// </summary>
public partial class NativeEngineTestWindow : Window
{
    private readonly WallpaperVideo _video;
    private readonly object _frameLock = new();
    private NativePlaybackEngine? _engine;
    private PendingFrame? _pendingFrame;
    private WriteableBitmap? _bitmap;
    private DateTime _lastFrameDetailsUpdate;
    private bool _frameRenderScheduled;
    private bool _isClosing;

    public NativeEngineTestWindow(WallpaperVideo video)
    {
        _video = video ?? throw new ArgumentNullException(nameof(video));
        InitializeComponent();
        VideoNameText.Text = _video.FullPath;
        Loaded += NativeEngineTestWindow_Loaded;
        Closing += NativeEngineTestWindow_Closing;
    }

    private void NativeEngineTestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _engine = new NativePlaybackEngine();
            _engine.FrameReady += Engine_FrameReady;
            _engine.Error += Engine_Error;
            _engine.SetLooping(true);
            _engine.Open(_video.FullPath);
            _engine.Play();

            EngineVersionText.Text = $"Native engine v{NativePlaybackEngine.Version}";
            PlaybackStateText.Text = "Playing — loop enabled";
            WaitingText.Text = "Waiting for first native frame…";
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }
    }

    private void Engine_FrameReady(object? sender, NativeFrameEventArgs e)
    {
        try
        {
            var width = checked((int)e.Width);
            var height = checked((int)e.Height);
            var stride = checked((int)e.Stride);
            var byteCount = checked(stride * height);

            if (width <= 0 || height <= 0 || stride < width * 4 || byteCount <= 0)
                return;

            var scheduleFrame = false;
            lock (_frameLock)
            {
                if (_isClosing || _frameRenderScheduled)
                    return;

                var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    Marshal.Copy(e.Pixels, buffer, 0, byteCount);
                    _pendingFrame = new PendingFrame(buffer, byteCount, width, height, stride, e.PresentationTimeHns);
                    _frameRenderScheduled = true;
                    scheduleFrame = true;
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
            }

            if (scheduleFrame)
                Dispatcher.BeginInvoke(RenderPendingFrame, DispatcherPriority.Render);
        }
        catch
        {
            ReleasePendingFrame();
            // A malformed frame is discarded. Native errors are surfaced through
            // the engine's error callback rather than allowed across the ABI.
        }
    }

    private void RenderPendingFrame()
    {
        PendingFrame? pending;
        lock (_frameLock)
        {
            pending = _pendingFrame;
            _pendingFrame = null;
            _frameRenderScheduled = false;
        }

        if (pending is null)
            return;

        try
        {
            if (_isClosing)
                return;

            if (_bitmap is null || _bitmap.PixelWidth != pending.Width || _bitmap.PixelHeight != pending.Height)
            {
                _bitmap = new WriteableBitmap(
                    pending.Width,
                    pending.Height,
                    96,
                    96,
                    PixelFormats.Bgr32,
                    null);
                NativePreview.Source = _bitmap;
            }

            _bitmap.WritePixels(
                new Int32Rect(0, 0, pending.Width, pending.Height),
                pending.Buffer,
                pending.ByteCount,
                pending.Stride,
                0);

            WaitingPanel.Visibility = Visibility.Collapsed;
            if (DateTime.UtcNow - _lastFrameDetailsUpdate > TimeSpan.FromMilliseconds(250))
            {
                FrameDetailsText.Text = $"{pending.Width} × {pending.Height}  •  BGRA32  •  {pending.Stride} bytes/row";
                _lastFrameDetailsUpdate = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            PlaybackStateText.Text = "Preview display error";
            FrameDetailsText.Text = ex.Message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pending.Buffer);
        }
    }

    private void Engine_Error(object? sender, NativeEngineErrorEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing)
                return;

            PlaybackStateText.Text = "Native decoder error";
            FrameDetailsText.Text = e.Message;
            WaitingText.Text = "The native engine could not decode this video.";
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
        });
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _engine?.Play();
            PlaybackStateText.Text = "Playing — loop enabled";
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex);
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _engine?.Pause();
            PlaybackStateText.Text = "Paused";
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _engine?.Stop();
            PlaybackStateText.Text = "Stopped";
            FrameDetailsText.Text = "Close and reopen this test window to decode the file again.";
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void NativeEngineTestWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        ReleasePendingFrame();

        if (_engine is not null)
        {
            _engine.FrameReady -= Engine_FrameReady;
            _engine.Error -= Engine_Error;
            _engine.Dispose();
            _engine = null;
        }
    }

    private void ReleasePendingFrame()
    {
        PendingFrame? pending;
        lock (_frameLock)
        {
            pending = _pendingFrame;
            _pendingFrame = null;
            _frameRenderScheduled = false;
        }

        if (pending is not null)
            ArrayPool<byte>.Shared.Return(pending.Buffer);
    }

    private void ShowStartupError(Exception ex)
    {
        EngineVersionText.Text = "Native engine unavailable";
        PlaybackStateText.Text = "Could not start the native engine";
        FrameDetailsText.Text = ex.Message;
        WaitingText.Text = "Build the x64 native engine and confirm that LoopWall.Engine.dll is beside LoopWall.exe.";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
    }

    private void ShowPlaybackError(Exception ex)
    {
        PlaybackStateText.Text = "Playback control error";
        FrameDetailsText.Text = ex.Message;
    }

    private sealed record PendingFrame(
        byte[] Buffer,
        int ByteCount,
        int Width,
        int Height,
        int Stride,
        long PresentationTimeHns);
}
