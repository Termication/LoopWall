using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LoopWall.App.Models;
using LoopWall.App.Services;

namespace LoopWall.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WallpaperVideo> _videos = [];
    private readonly SettingsService _settingsService = new();
    private readonly VideoLibraryService _libraryService = new();
    private AppSettings _settings = new();
    private bool _isPlaying;

    public MainWindow()
    {
        InitializeComponent();
        VideoList.ItemsSource = _videos;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();

        foreach (var video in _libraryService.CreateEntries(_settings.VideoPaths))
            _videos.Add(video);

        UpdateLibraryCount();

        var last = _videos.FirstOrDefault(v =>
            string.Equals(v.FullPath, _settings.LastSelectedVideo, StringComparison.OrdinalIgnoreCase));

        if (last is not null)
            VideoList.SelectedItem = last;

        StatusText.Text = "Library loaded";
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _settings.VideoPaths = _videos.Select(v => v.FullPath).ToList();
            _settings.LastSelectedVideo = (VideoList.SelectedItem as WallpaperVideo)?.FullPath;
            await _settingsService.SaveAsync(_settings);
        }
        catch
        {
            // Closing should not be blocked by a settings write failure.
        }

        VideoPreview.Stop();
        VideoPreview.Source = null;
    }

    private async void ImportVideosButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import videos into LoopWall",
            Filter = "Video files|*.mp4;*.wmv;*.avi;*.mov;*.mkv;*.webm;*.mpeg;*.mpg|All files|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var existing = new HashSet<string>(_videos.Select(v => v.FullPath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var video in _libraryService.CreateEntries(dialog.FileNames))
        {
            if (existing.Add(video.FullPath))
                _videos.Add(video);
        }

        UpdateLibraryCount();
        _settings.VideoPaths = _videos.Select(v => v.FullPath).ToList();
        await _settingsService.SaveAsync(_settings);

        StatusText.Text = $"Imported {dialog.FileNames.Length} selected file(s)";
        if (VideoList.SelectedItem is null && _videos.Count > 0)
            VideoList.SelectedIndex = 0;
    }

    private void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoList.SelectedItem is not WallpaperVideo selected)
            return;

        try
        {
            VideoPreview.Stop();
            VideoPreview.Source = new Uri(selected.FullPath, UriKind.Absolute);
            EmptyPreview.Visibility = Visibility.Collapsed;
            PreviewStateText.Text = "LOADING";
            SelectedVideoName.Text = selected.FileName;
            SelectedVideoDetails.Text = $"{selected.Extension}  •  {selected.FullPath}";
            PlayPauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            NativeEngineTestButton.IsEnabled = File.Exists(selected.FullPath);
            _isPlaying = false;
            PlayPauseButton.Content = "Play";
            StatusText.Text = "Opening video preview...";
        }
        catch (Exception ex)
        {
            ShowPlaybackError($"Could not open this video: {ex.Message}");
        }
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        var duration = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan.ToString(@"hh\:mm\:ss")
            : "Unknown duration";

        SelectedVideoDetails.Text = $"{duration}  •  Preview ready";
        PreviewStateText.Text = "READY";
        PlayPauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        VideoPreview.Volume = _settings.MutePreview ? 0 : Math.Clamp(_settings.PreviewVolume / 100.0, 0, 1);
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Play();
        _isPlaying = true;
        PlayPauseButton.Content = "Pause";
        PreviewStateText.Text = "PLAYING";
        StatusText.Text = "Preview playing";
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        // Loop the preview. The final wallpaper engine will use a more controlled
        // frame pipeline to improve loop transitions and resource usage.
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Play();
        _isPlaying = true;
        PlayPauseButton.Content = "Pause";
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ShowPlaybackError(e.ErrorException?.Message ?? "The Windows media player could not decode this file.");
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is null)
            return;

        if (_isPlaying)
        {
            VideoPreview.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "Play";
            PreviewStateText.Text = "PAUSED";
            StatusText.Text = "Preview paused";
        }
        else
        {
            VideoPreview.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "Pause";
            PreviewStateText.Text = "PLAYING";
            StatusText.Text = "Preview playing";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is null)
            return;

        VideoPreview.Stop();
        _isPlaying = false;
        PlayPauseButton.Content = "Play";
        PreviewStateText.Text = "STOPPED";
        StatusText.Text = "Preview stopped";
    }

    private void NativeEngineTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (VideoList.SelectedItem is not WallpaperVideo selected)
            return;

        try
        {
            var testWindow = new Views.NativeEngineTestWindow(selected)
            {
                Owner = this
            };
            testWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not open the native engine test window: {ex.Message}",
                "LoopWall native engine", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateLibraryCount()
    {
        LibraryCountText.Text = $"{_videos.Count} video{(_videos.Count == 1 ? "" : "s")}";
    }

    private void ShowPlaybackError(string message)
    {
        _isPlaying = false;
        PlayPauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        PreviewStateText.Text = "UNAVAILABLE";
        EmptyPreview.Visibility = Visibility.Visible;
        StatusText.Text = "Video preview failed";
        MessageBox.Show(this,
            $"{message}\n\nSome formats or codecs may not be supported by the Windows preview player. We will improve format support in the native playback engine.",
            "LoopWall video preview", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
