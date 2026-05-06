using System.Diagnostics;
using System.Text;

namespace MediaScribeRecorder.Services;

public sealed class TranscriptionService
{
    private readonly PortableAppPaths paths;
    private readonly LogService log;

    public TranscriptionService(PortableAppPaths paths, LogService log)
    {
        this.paths = paths;
        this.log = log;
    }

    public string WhisperPath => Path.Combine(paths.Tools, "whisper-cli.exe");
    public string FfmpegPath => Path.Combine(paths.Tools, "ffmpeg.exe");
    public string FfprobePath => Path.Combine(paths.Tools, "ffprobe.exe");
    public string ModelPath(string model) => Path.Combine(paths.Models, ModelFileName(model));

    public bool IsReady(string model) => File.Exists(WhisperPath) && File.Exists(FfmpegPath) && File.Exists(ModelPath(model));

    public string MissingToolsMessage(string model)
    {
        var missing = new List<string>();
        if (!File.Exists(WhisperPath)) missing.Add("Tools\\whisper-cli.exe");
        if (!File.Exists(FfmpegPath)) missing.Add("Tools\\ffmpeg.exe");
        if (!File.Exists(ModelPath(model))) missing.Add("Models\\" + ModelFileName(model));
        return missing.Count == 0 ? "" : "Transcription indisponible: " + string.Join(", ", missing);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        string language,
        string model,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
        {
            throw new FileNotFoundException("Fichier audio introuvable.", mediaPath);
        }

        if (!IsReady(model))
        {
            throw new InvalidOperationException(MissingToolsMessage(model));
        }

        var outputPrefix = Path.Combine(
            Path.GetDirectoryName(mediaPath) ?? paths.Recordings,
            Path.GetFileNameWithoutExtension(mediaPath));
        var transcriptPath = outputPrefix + ".txt";

        var tempDir = Path.Combine(Path.GetTempPath(), "mediascribe-recorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var preparedWav = Path.Combine(tempDir, "audio.wav");

        try
        {
            progress?.Report("Préparation audio");
            await RunProcessAsync(
                FfmpegPath,
                [
                    "-y",
                    "-i", mediaPath,
                    "-vn",
                    "-ac", "1",
                    "-ar", "16000",
                    preparedWav,
                ],
                progress,
                cancellationToken).ConfigureAwait(false);

            var selectedModel = ModelPath(model);
            progress?.Report($"Transcription Whisper {NormalizeModel(model)}");
            var whisperPrefix = Path.Combine(tempDir, "transcript");
            var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "fr" : language.Trim();
            var whisperArgs = new List<string>
            {
                "-m", selectedModel,
                "-f", preparedWav,
            };
            if (!normalizedLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                whisperArgs.Add("-l");
                whisperArgs.Add(normalizedLanguage);
            }
            whisperArgs.AddRange(["-otxt", "-of", whisperPrefix, "-pp"]);

            await RunProcessAsync(
                WhisperPath,
                whisperArgs,
                progress,
                cancellationToken).ConfigureAwait(false);

            var generated = whisperPrefix + ".txt";
            if (!File.Exists(generated))
            {
                throw new InvalidOperationException("whisper.cpp n'a pas produit de fichier transcript.");
            }

            var text = (await File.ReadAllTextAsync(generated, Encoding.UTF8, cancellationToken).ConfigureAwait(false)).Trim();
            await File.WriteAllTextAsync(transcriptPath, text + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            progress?.Report("Terminé");
            return new TranscriptionResult(mediaPath, transcriptPath, text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error(ex, $"Transcription failed for {mediaPath}");
            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            output.AppendLine(e.Data);
            progress?.Report(CompactStatus(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            output.AppendLine(e.Data);
            progress?.Report(CompactStatus(e.Data));
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Impossible de démarrer {Path.GetFileName(fileName)}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} a échoué: {Tail(output.ToString(), 2500)}");
        }
    }

    private static string CompactStatus(string line)
    {
        line = line.Trim();
        if (line.Length <= 140) return line;
        return line[..140] + "...";
    }

    private static string Tail(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[^maxLength..];
    }

    private static string NormalizeModel(string model)
    {
        return model.Equals("medium", StringComparison.OrdinalIgnoreCase) ? "medium" : "small";
    }

    private static string ModelFileName(string model)
    {
        return NormalizeModel(model) == "medium" ? "ggml-medium.bin" : "ggml-small.bin";
    }
}

public sealed record TranscriptionResult(string MediaPath, string TranscriptPath, string Text);
