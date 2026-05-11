using System.Windows;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MediaScribeRecorder.Models;
using MediaScribeRecorder.Services;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaScribeRecorder;

public partial class MainWindow : Window
{
    private readonly PortableAppPaths paths = new();
    private readonly SettingsStore settingsStore;
    private readonly HistoryStore historyStore;
    private readonly LogService log;
    private readonly AudioDeviceService audioDevices = new();
    private readonly WindowProcessService processWindows = new();
    private readonly TranscriptionService transcription;
    private RecordingSettings settings;
    private RecordingSession? session;
    private CancellationTokenSource? transcriptionCts;
    private readonly ObservableCollection<TranscriptionHistoryItem> historyItems = [];
    private readonly MediaPlayer previewPlayer = new();
    private readonly DispatcherTimer previewTimer = new();
    private string? currentTranscriptPath;
    private string? currentMediaPath;
    private string? previewMediaPath;
    private double displayedSystemLevel;
    private double displayedMicrophoneLevel;
    private bool isPreviewPlaying;
    private bool isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        FitWindowToWorkArea();
        settingsStore = new SettingsStore(paths);
        historyStore = new HistoryStore(paths);
        log = new LogService(paths);
        transcription = new TranscriptionService(paths, log);
        previewTimer.Interval = TimeSpan.FromMilliseconds(250);
        previewTimer.Tick += OnPreviewTimerTick;
        previewPlayer.MediaOpened += OnPreviewMediaOpened;
        previewPlayer.MediaEnded += OnPreviewMediaEnded;
        settings = settingsStore.Load();
        OutputFolderTextBox.Text = settings.OutputFolder;
        FooterText.Text = $"Application: {paths.Root} | Données: {paths.UserRoot}";

        LoadMicrophones();
        LoadProcesses();
        LoadLanguages();
        IncludeSystemAudioCheckBox.IsChecked = settings.IncludeSystemAudio;
        ApplicationModeRadio.IsChecked = settings.LastSystemMode.Equals("application", StringComparison.OrdinalIgnoreCase);
        DesktopModeRadio.IsChecked = ApplicationModeRadio.IsChecked != true;
        UpdateModeState();
        UpdateTranscriptionAvailability();
        LoadHistory();
        isInitialized = true;
    }

    private void FitWindowToWorkArea()
    {
        const double screenPadding = 32;
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(MinHeight, workArea.Height - screenPadding);
        MaxWidth = Math.Max(MinWidth, workArea.Width - screenPadding);
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
    }

    private void LoadLanguages()
    {
        var languages = new[]
        {
            new LanguageOption("fr", "Français"),
            new LanguageOption("en", "Anglais"),
            new LanguageOption("es", "Espagnol"),
            new LanguageOption("de", "Allemand"),
            new LanguageOption("it", "Italien"),
            new LanguageOption("pt", "Portugais"),
            new LanguageOption("nl", "Néerlandais"),
            new LanguageOption("auto", "Auto-détection"),
        };
        LanguageComboBox.ItemsSource = languages;
        LanguageComboBox.SelectedValue = string.IsNullOrWhiteSpace(settings.TranscriptionLanguage) ? "fr" : settings.TranscriptionLanguage;
        if (LanguageComboBox.SelectedItem is null)
        {
            LanguageComboBox.SelectedValue = "fr";
        }
    }

    private void LoadMicrophones()
    {
        var devices = audioDevices.GetCaptureDevices();
        MicrophoneComboBox.ItemsSource = devices;
        MicrophoneComboBox.SelectedValue = settings.MicrophoneDeviceId;
        if (MicrophoneComboBox.SelectedItem is null && devices.Count > 0)
        {
            MicrophoneComboBox.SelectedIndex = 0;
        }
    }

    private void LoadProcesses()
    {
        var sources = processWindows.GetVisibleProcessWindows();
        ProcessComboBox.ItemsSource = sources;
        if (sources.Count > 0)
        {
            ProcessComboBox.SelectedIndex = 0;
        }
    }

    private void OnRefreshProcesses(object sender, RoutedEventArgs e) => LoadProcesses();

    private void OnSystemModeChanged(object sender, RoutedEventArgs e) => UpdateModeState();

    private void OnIncludeSystemAudioChanged(object sender, RoutedEventArgs e)
    {
        UpdateModeState();
        if (isInitialized)
        {
            SaveCurrentSettings();
        }
    }

    private void UpdateModeState()
    {
        if (ProcessComboBox is null || SystemModePanel is null || RefreshProcessesButton is null)
        {
            return;
        }

        var includeSystemAudio = IncludeSystemAudioCheckBox?.IsChecked == true;
        SystemModePanel.IsEnabled = includeSystemAudio;
        ProcessComboBox.IsEnabled = includeSystemAudio && ApplicationModeRadio.IsChecked == true;
        RefreshProcessesButton.IsEnabled = includeSystemAudio && ApplicationModeRadio.IsChecked == true;
    }

    private void OnChooseOutputFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de sortie",
            InitialDirectory = Directory.Exists(OutputFolderTextBox.Text) ? OutputFolderTextBox.Text : paths.Recordings,
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderTextBox.Text = dialog.FolderName;
            SaveCurrentSettings();
        }
    }

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCurrentSettings();
            PathValidator.EnsureWritableDirectory(settings.OutputFolder);

            var recordingFolder = CreateRecordingFolder(settings.OutputFolder);
            var outputPath = Path.Combine(recordingFolder, "mix.wav");
            var systemOutputPath = settings.SaveSeparateTracks ? Path.Combine(recordingFolder, "windows.wav") : null;
            var microphoneOutputPath = settings.SaveSeparateTracks ? Path.Combine(recordingFolder, "micro.wav") : null;
            var micDevice = audioDevices.GetCaptureDevice(settings.MicrophoneDeviceId);
            IAudioCaptureSource micCapture = new WasapiCaptureSource(micDevice);
            IAudioCaptureSource systemCapture;
            ProcessLoopbackCaptureSource? processCapture = null;

            if (IncludeSystemAudioCheckBox.IsChecked != true)
            {
                systemCapture = new SilentCaptureSource();
            }
            else if (ApplicationModeRadio.IsChecked == true)
            {
                if (ProcessComboBox.SelectedItem is not ProcessAudioSource selectedProcess)
                {
                    throw new InvalidOperationException("Choisissez une application à enregistrer.");
                }

                processCapture = new ProcessLoopbackCaptureSource(selectedProcess.ProcessId);
                systemCapture = processCapture;
            }
            else
            {
                systemCapture = new WasapiLoopbackCaptureSource(audioDevices.GetDefaultRenderDevice());
            }

            session = new RecordingSession(systemCapture, micCapture, outputPath, systemOutputPath, microphoneOutputPath, log, settings.SystemGain, settings.MicrophoneGain);
            session.LevelsUpdated += OnLevelsUpdated;
            session.WarningRaised += (_, warning) => Dispatcher.Invoke(() => StatusText.Text = warning);
            session.Start();

            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Enregistrement";
            CurrentFileText.Text = outputPath;

            if (processCapture is not null && !await processCapture.WaitForAudioAsync(TimeSpan.FromSeconds(3), CancellationToken.None))
            {
                var warning = "REC-APP-002 - Capture application démarrée, mais aucun son n'a été reçu. Lancez du son dans cette application ou utilisez Tout le bureau.";
                log.Info(warning);
                await StopCurrentSession(returnOutput: false);
                MessageBox.Show(this, warning, "Capture application", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to start recording.");
            await StopCurrentSession(returnOutput: false);
            MessageBox.Show(this, ex.Message, "MediaScribe Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        var outputPath = await StopCurrentSession(returnOutput: true);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            SetPreviewMedia(outputPath);
            if (settings.AutoTranscribeAfterRecording)
            {
                await StartTranscriptionAsync(outputPath);
            }
            else
            {
                currentMediaPath = outputPath;
                RetryTranscriptionButton.IsEnabled = true;
                SetTranscriptionProgress("Audio prêt, transcription automatique désactivée", 0);
            }
        }
    }

    private async Task<string?> StopCurrentSession(bool returnOutput)
    {
        var active = session;
        session = null;
        string? outputPath = null;
        if (active is not null)
        {
            outputPath = active.OutputPath;
            await active.StopAsync();
            active.Dispose();
        }

        RecordButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Prêt";
        SystemLevelBar.Value = 0;
        MicLevelBar.Value = 0;
        SystemLevelText.Text = "0%";
        MicLevelText.Text = "0%";
        displayedSystemLevel = 0;
        displayedMicrophoneLevel = 0;
        return returnOutput ? outputPath : null;
    }

    private void OnLevelsUpdated(object? sender, RecordingLevelsEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            displayedSystemLevel = Smooth(displayedSystemLevel, e.SystemLevel);
            displayedMicrophoneLevel = Smooth(displayedMicrophoneLevel, e.MicrophoneLevel);
            SystemLevelBar.Value = displayedSystemLevel;
            MicLevelBar.Value = displayedMicrophoneLevel;
            SystemLevelText.Text = $"{Math.Round(displayedSystemLevel * 100):0}%";
            MicLevelText.Text = $"{Math.Round(displayedMicrophoneLevel * 100):0}%";
        });
    }

    private static double Smooth(double current, double target)
    {
        var alpha = target > current ? 0.28 : 0.08;
        return current + (target - current) * alpha;
    }

    private void SaveCurrentSettings()
    {
        settings.OutputFolder = string.IsNullOrWhiteSpace(OutputFolderTextBox.Text)
            ? paths.Recordings
            : OutputFolderTextBox.Text.Trim();
        settings.MicrophoneDeviceId = MicrophoneComboBox.SelectedValue as string ?? "";
        settings.LastSystemMode = ApplicationModeRadio.IsChecked == true ? "application" : "desktop";
        settings.IncludeSystemAudio = IncludeSystemAudioCheckBox.IsChecked == true;
        settings.TranscriptionLanguage = LanguageComboBox.SelectedValue as string ?? "fr";
        settingsStore.Save(settings);
    }

    private void OnLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings is null || LanguageComboBox.SelectedValue is null)
        {
            return;
        }

        SaveCurrentSettings();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
        var dialog = new SettingsWindow(settings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            settings.SystemGain = dialog.Settings.SystemGain;
            settings.MicrophoneGain = dialog.Settings.MicrophoneGain;
            settings.WhisperModel = dialog.Settings.WhisperModel;
            settings.AutoTranscribeAfterRecording = dialog.Settings.AutoTranscribeAfterRecording;
            settings.SaveSeparateTracks = dialog.Settings.SaveSeparateTracks;
            settingsStore.Save(settings);
            UpdateTranscriptionAvailability();
        }
    }

    private async void OnImportTranscribe(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importer un fichier audio ou vidéo",
            Filter = "Audio et vidéo|*.wav;*.mp3;*.mp4;*.m4a;*.aac;*.flac;*.ogg;*.webm;*.mkv;*.mov;*.avi|Tous les fichiers|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await StartTranscriptionAsync(dialog.FileName);
        }
    }

    private async Task StartTranscriptionAsync(string mediaPath)
    {
        if (transcriptionCts is not null)
        {
            MessageBox.Show(this, "Une transcription est déjà en cours.", "MediaScribe Recorder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveCurrentSettings();
        if (!transcription.IsReady(settings.WhisperModel))
        {
            var message = transcription.MissingToolsMessage(settings.WhisperModel);
            TranscriptionStatusText.Text = message;
            log.Info(message);
            return;
        }

        transcriptionCts = new CancellationTokenSource();
        SetTranscriptionBusy(true);
        SetTranscriptionProgress("Préparation transcription", 0);
        TranscriptTextBox.Text = "";
        TranscriptFileText.Text = "";
        SuspiciousTranscriptionText.Visibility = Visibility.Collapsed;
        SuspiciousTranscriptionText.Text = "";
        currentTranscriptPath = null;
        currentMediaPath = mediaPath;
        CopyTranscriptButton.IsEnabled = false;
        OpenTranscriptButton.IsEnabled = false;
        RetryTranscriptionButton.IsEnabled = false;
        TranscriptionStatusText.Text = $"Transcription de {Path.GetFileName(mediaPath)}";
        SetPreviewMedia(mediaPath);

        var progress = new Progress<TranscriptionProgress>(state =>
        {
            if (state.Percent.HasValue)
            {
                SetTranscriptionProgress(state.Message, state.Percent.Value);
            }
            else if (!string.IsNullOrWhiteSpace(state.Message))
            {
                TranscriptionStatusText.Text = state.Message;
            }
        });

        try
        {
            var language = LanguageComboBox.SelectedValue as string ?? "fr";
            if (language == "auto")
            {
                language = "auto";
            }

            var result = await transcription.TranscribeAsync(mediaPath, language, settings.WhisperModel, progress, transcriptionCts.Token);
            TranscriptTextBox.Text = result.Text;
            currentTranscriptPath = result.TranscriptPath;
            TranscriptFileText.Text = result.TranscriptPath;
            SetTranscriptionProgress(result.IsSuspicious ? "Transcription terminée avec avertissement" : "Transcription terminée", 100);
            if (result.IsSuspicious)
            {
                SuspiciousTranscriptionText.Text = "Transcription suspecte: " + result.SuspicionReason;
                SuspiciousTranscriptionText.Visibility = Visibility.Visible;
            }

            CopyTranscriptButton.IsEnabled = !string.IsNullOrWhiteSpace(result.Text);
            OpenTranscriptButton.IsEnabled = File.Exists(result.TranscriptPath);
            AddHistoryItem(result, language, settings.WhisperModel);
        }
        catch (OperationCanceledException)
        {
            SetTranscriptionProgress("Transcription annulée", 0);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to transcribe.");
            SetTranscriptionProgress(ex.Message, 0);
            MessageBox.Show(this, ex.Message, "Transcription", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            transcriptionCts?.Dispose();
            transcriptionCts = null;
            SetTranscriptionBusy(false);
            RetryTranscriptionButton.IsEnabled = !string.IsNullOrWhiteSpace(currentMediaPath) && File.Exists(currentMediaPath);
        }
    }

    private async void OnRetryTranscription(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(currentMediaPath) && File.Exists(currentMediaPath))
        {
            await StartTranscriptionAsync(currentMediaPath);
        }
    }

    private void OnCancelTranscription(object sender, RoutedEventArgs e)
    {
        transcriptionCts?.Cancel();
    }

    private void OnCopyTranscript(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TranscriptTextBox.Text))
        {
            Clipboard.SetText(TranscriptTextBox.Text);
            TranscriptionStatusText.Text = "Transcript copié";
        }
    }

    private void OnOpenTranscript(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentTranscriptPath) || !File.Exists(currentTranscriptPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = currentTranscriptPath,
            UseShellExecute = true,
        });
    }

    private void SetTranscriptionBusy(bool busy)
    {
        ImportTranscribeButton.IsEnabled = !busy;
        CancelTranscriptionButton.IsEnabled = busy;
        LanguageComboBox.IsEnabled = !busy;
        RetryTranscriptionButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(currentMediaPath) && File.Exists(currentMediaPath);
    }

    private void UpdateTranscriptionAvailability()
    {
        var model = string.IsNullOrWhiteSpace(settings.WhisperModel) ? "small" : settings.WhisperModel;
        var ready = transcription.IsReady(model);
        SetTranscriptionProgress(
            ready ? $"Transcription prête ({model})" : $"{transcription.MissingToolsCode(model)} - {transcription.MissingToolsMessage(model)}",
            0);
    }

    private void SetTranscriptionProgress(string message, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        TranscriptionProgressBar.Value = percent;
        TranscriptionPercentText.Text = $"{percent}%";
        TranscriptionStatusText.Text = message;
    }

    private void LoadHistory()
    {
        historyItems.Clear();
        foreach (var item in historyStore.Load())
        {
            historyItems.Add(item);
        }

        HistoryListBox.ItemsSource = historyItems;
        UpdateHistoryEmptyState();
    }

    private void AddHistoryItem(TranscriptionResult result, string language, string model)
    {
        var mediaFolder = Path.GetDirectoryName(result.MediaPath) ?? "";
        var mixPath = Path.GetFileName(result.MediaPath).Equals("mix.wav", StringComparison.OrdinalIgnoreCase)
            ? result.MediaPath
            : "";
        var microphonePath = string.IsNullOrWhiteSpace(mediaFolder) ? "" : Path.Combine(mediaFolder, "micro.wav");
        var systemPath = string.IsNullOrWhiteSpace(mediaFolder) ? "" : Path.Combine(mediaFolder, "windows.wav");
        var item = new TranscriptionHistoryItem
        {
            FileName = Path.GetFileName(result.MediaPath),
            MediaPath = result.MediaPath,
            MixPath = mixPath,
            MicrophonePath = File.Exists(microphonePath) ? microphonePath : "",
            SystemPath = File.Exists(systemPath) ? systemPath : "",
            TranscriptPath = result.TranscriptPath,
            RecordingFolder = mediaFolder,
            Language = language,
            Model = model,
            IsSuspicious = result.IsSuspicious,
            SuspicionReason = result.SuspicionReason,
            CreatedAt = DateTime.Now,
        };

        historyItems.Clear();
        foreach (var saved in historyStore.Add(item))
        {
            historyItems.Add(saved);
        }

        UpdateHistoryEmptyState();
    }

    private void UpdateHistoryEmptyState()
    {
        HistoryEmptyText.Visibility = historyItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryListBox.Visibility = historyItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenHistoryMedia(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranscriptionHistoryItem item)
        {
            OpenPath(item.MediaPath);
        }
    }

    private void OnOpenHistoryTranscript(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranscriptionHistoryItem item)
        {
            OpenPath(item.TranscriptPath);
        }
    }

    private void OnOpenHistoryFolder(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranscriptionHistoryItem item)
        {
            var folder = !string.IsNullOrWhiteSpace(item.RecordingFolder)
                ? item.RecordingFolder
                : Path.GetDirectoryName(item.TranscriptPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                OpenPath(folder);
            }
        }
    }

    private void OnPreviewHistoryMedia(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranscriptionHistoryItem item)
        {
            SetPreviewMedia(item.PreviewPath);
            PlayPreview();
        }
    }

    private void OnPreviewPlayPause(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(previewMediaPath) || !File.Exists(previewMediaPath))
        {
            return;
        }

        if (isPreviewPlaying)
        {
            previewPlayer.Pause();
            previewTimer.Stop();
            isPreviewPlaying = false;
            PreviewPlayPauseButton.Content = "Lecture";
            return;
        }

        PlayPreview();
    }

    private void PlayPreview()
    {
        if (string.IsNullOrWhiteSpace(previewMediaPath) || !File.Exists(previewMediaPath))
        {
            return;
        }

        previewPlayer.Play();
        previewTimer.Start();
        isPreviewPlaying = true;
        PreviewPlayPauseButton.Content = "Pause";
    }

    private void SetPreviewMedia(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return;
        }

        if (previewMediaPath?.Equals(mediaPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        previewPlayer.Stop();
        previewTimer.Stop();
        previewMediaPath = mediaPath;
        isPreviewPlaying = false;
        PreviewPlayPauseButton.Content = "Lecture";
        PreviewPlayPauseButton.IsEnabled = true;
        PreviewTimelineSlider.IsEnabled = true;
        PreviewTimelineSlider.Value = 0;
        PreviewStatusText.Text = Path.GetFileName(mediaPath);
        PreviewTimeText.Text = "00:00 / 00:00";
        previewPlayer.Open(new Uri(mediaPath, UriKind.Absolute));
    }

    private void OnPreviewMediaOpened(object? sender, EventArgs e)
    {
        if (previewPlayer.NaturalDuration.HasTimeSpan)
        {
            PreviewTimelineSlider.Maximum = Math.Max(1, previewPlayer.NaturalDuration.TimeSpan.TotalSeconds);
            PreviewTimeText.Text = $"00:00 / {FormatTime(previewPlayer.NaturalDuration.TimeSpan)}";
        }
    }

    private void OnPreviewMediaEnded(object? sender, EventArgs e)
    {
        previewTimer.Stop();
        isPreviewPlaying = false;
        PreviewPlayPauseButton.Content = "Lecture";
        previewPlayer.Position = TimeSpan.Zero;
        PreviewTimelineSlider.Value = 0;
        UpdatePreviewTime();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        UpdatePreviewTime();
    }

    private void OnPreviewTimelineSeek(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (previewMediaPath is null || !PreviewTimelineSlider.IsEnabled)
        {
            return;
        }

        previewPlayer.Position = TimeSpan.FromSeconds(PreviewTimelineSlider.Value);
        UpdatePreviewTime();
    }

    private void UpdatePreviewTime()
    {
        var position = previewPlayer.Position;
        var duration = previewPlayer.NaturalDuration.HasTimeSpan ? previewPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
        PreviewTimelineSlider.Value = Math.Clamp(position.TotalSeconds, PreviewTimelineSlider.Minimum, PreviewTimelineSlider.Maximum);
        PreviewTimeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private static string CreateRecordingFolder(string outputRoot)
    {
        var safeRoot = string.IsNullOrWhiteSpace(outputRoot) ? Environment.CurrentDirectory : outputRoot;
        var baseName = "MediaScribe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(safeRoot, baseName);
        var index = 2;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(safeRoot, $"{baseName}-{index}");
            index++;
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    protected override async void OnClosed(EventArgs e)
    {
        transcriptionCts?.Cancel();
        previewTimer.Stop();
        previewPlayer.Close();
        await StopCurrentSession(returnOutput: false);
        base.OnClosed(e);
    }
}
