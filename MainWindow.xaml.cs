using System.Windows;
using System.Diagnostics;
using MediaScribeRecorder.Models;
using MediaScribeRecorder.Services;
using Microsoft.Win32;

namespace MediaScribeRecorder;

public partial class MainWindow : Window
{
    private readonly PortableAppPaths paths = new();
    private readonly SettingsStore settingsStore;
    private readonly LogService log;
    private readonly AudioDeviceService audioDevices = new();
    private readonly WindowProcessService processWindows = new();
    private readonly TranscriptionService transcription;
    private RecordingSettings settings;
    private RecordingSession? session;
    private CancellationTokenSource? transcriptionCts;
    private string? currentTranscriptPath;
    private double displayedSystemLevel;
    private double displayedMicrophoneLevel;
    private bool isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        settingsStore = new SettingsStore(paths);
        log = new LogService(paths);
        transcription = new TranscriptionService(paths, log);
        settings = settingsStore.Load();
        OutputFolderTextBox.Text = settings.OutputFolder;
        FooterText.Text = $"Portable: {paths.Root}";

        LoadMicrophones();
        LoadProcesses();
        LoadLanguages();
        IncludeSystemAudioCheckBox.IsChecked = settings.IncludeSystemAudio;
        ApplicationModeRadio.IsChecked = settings.LastSystemMode.Equals("application", StringComparison.OrdinalIgnoreCase);
        DesktopModeRadio.IsChecked = ApplicationModeRadio.IsChecked != true;
        UpdateModeState();
        UpdateTranscriptionAvailability();
        isInitialized = true;
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
            Directory.CreateDirectory(settings.OutputFolder);

            var outputPath = Path.Combine(settings.OutputFolder, $"MediaScribe-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            var micDevice = audioDevices.GetCaptureDevice(settings.MicrophoneDeviceId);
            IAudioCaptureSource micCapture = new WasapiCaptureSource(micDevice);
            IAudioCaptureSource systemCapture;

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

                systemCapture = new ProcessLoopbackCaptureSource(selectedProcess.ProcessId);
            }
            else
            {
                systemCapture = new WasapiLoopbackCaptureSource(audioDevices.GetDefaultRenderDevice());
            }

            session = new RecordingSession(systemCapture, micCapture, outputPath, log, settings.SystemGain, settings.MicrophoneGain);
            session.LevelsUpdated += OnLevelsUpdated;
            session.WarningRaised += (_, warning) => Dispatcher.Invoke(() => StatusText.Text = warning);
            session.Start();

            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Enregistrement";
            CurrentFileText.Text = outputPath;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to start recording.");
            await StopCurrentSession(transcribeAfterStop: false);
            MessageBox.Show(this, ex.Message, "MediaScribe Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        var outputPath = await StopCurrentSession(transcribeAfterStop: true);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await StartTranscriptionAsync(outputPath);
        }
    }

    private async Task<string?> StopCurrentSession(bool transcribeAfterStop)
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
        return transcribeAfterStop ? outputPath : null;
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
        TranscriptTextBox.Text = "";
        TranscriptFileText.Text = "";
        currentTranscriptPath = null;
        TranscriptionStatusText.Text = $"Transcription de {Path.GetFileName(mediaPath)}";

        var progress = new Progress<string>(message =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                TranscriptionStatusText.Text = message;
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
            TranscriptionStatusText.Text = "Transcription terminée";
            CopyTranscriptButton.IsEnabled = !string.IsNullOrWhiteSpace(result.Text);
            OpenTranscriptButton.IsEnabled = File.Exists(result.TranscriptPath);
        }
        catch (OperationCanceledException)
        {
            TranscriptionStatusText.Text = "Transcription annulée";
        }
        catch (Exception ex)
        {
            log.Error(ex, "Unable to transcribe.");
            TranscriptionStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Transcription", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            transcriptionCts?.Dispose();
            transcriptionCts = null;
            SetTranscriptionBusy(false);
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
    }

    private void UpdateTranscriptionAvailability()
    {
        var model = string.IsNullOrWhiteSpace(settings.WhisperModel) ? "small" : settings.WhisperModel;
        TranscriptionStatusText.Text = transcription.IsReady(model)
            ? $"Transcription prête ({model})"
            : transcription.MissingToolsMessage(model);
    }

    protected override async void OnClosed(EventArgs e)
    {
        transcriptionCts?.Cancel();
        await StopCurrentSession(transcribeAfterStop: false);
        base.OnClosed(e);
    }
}
