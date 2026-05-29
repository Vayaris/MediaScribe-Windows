using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace MediaScribeRecorder.Services;

public sealed class TranscriptionService
{
    private static readonly string[] SuspiciousPhrases =
    [
        "Sous-titres réalisés par la communauté d'Amara.org",
        "Sous-titres réalisés par la communauté d’Amara.org",
        "*Musique d'outro*",
        "Musique d'outro",
        "Alright !",
        "Alright!",
    ];

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

    public string MissingToolsCode(string model)
    {
        if (!File.Exists(ModelPath(model)))
        {
            return "TRN-MODEL-001";
        }

        return "TRN-TOOL-001";
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string mediaPath,
        string language,
        string model,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
        {
            throw new UserFacingException("TRN-INPUT-001", $"Fichier audio introuvable: {mediaPath}");
        }

        if (!IsReady(model))
        {
            throw new UserFacingException(MissingToolsCode(model), MissingToolsMessage(model));
        }

        if (TryGetSeparateTracks(mediaPath, out var microphonePath, out var systemPath))
        {
            try
            {
                return await TranscribeSeparatedSourcesAsync(mediaPath, microphonePath, systemPath, language, model, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Error(ex, $"Source-separated transcription failed for {mediaPath}. Falling back to mix.");
                progress?.Report(new TranscriptionProgress("Séparation des sources impossible, transcription du mix", 10, "fallback"));
                var fallback = await TranscribeSingleAsync(mediaPath, language, model, progress, cancellationToken).ConfigureAwait(false);
                return fallback with
                {
                    IsSuspicious = true,
                    SuspicionReason = string.IsNullOrWhiteSpace(fallback.SuspicionReason)
                        ? "Transcription par source impossible, fallback sur le mix."
                        : fallback.SuspicionReason + " Transcription par source impossible, fallback sur le mix.",
                    TranscriptMode = "Transcript normal",
                };
            }
        }

        return await TranscribeSingleAsync(mediaPath, language, model, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranscriptionResult> TranscribeSingleAsync(
        string mediaPath,
        string language,
        string model,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outputPrefix = Path.Combine(
            Path.GetDirectoryName(mediaPath) ?? paths.Recordings,
            Path.GetFileNameWithoutExtension(mediaPath));
        var transcriptPath = outputPrefix + ".txt";

        var tempDir = Path.Combine(Path.GetTempPath(), "mediascribe-recorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var preparedWav = Path.Combine(tempDir, "audio.wav");

        try
        {
            var duration = await GetMediaDurationAsync(mediaPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TranscriptionProgress("Préparation audio", 0, "prepare"));
            await RunProcessAsync(
                FfmpegPath,
                [
                    "-y",
                    "-i", mediaPath,
                    "-progress", "pipe:1",
                    "-nostats",
                    "-vn",
                    "-ac", "1",
                    "-ar", "16000",
                    preparedWav,
                ],
                progress,
                line => ParseFfmpegProgress(line, duration),
                cancellationToken).ConfigureAwait(false);

            var selectedModel = ModelPath(model);
            progress?.Report(new TranscriptionProgress($"Transcription Whisper {NormalizeModel(model)}", 15, "whisper"));
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
            whisperArgs.Add("-sns");
            if (NormalizeModel(model) == "medium")
            {
                whisperArgs.Add("-nf");
            }
            else
            {
                whisperArgs.Add("-lpt");
                whisperArgs.Add("-0.30");
            }

            whisperArgs.AddRange(["-otxt", "-of", whisperPrefix, "-pp"]);

            await RunProcessAsync(
                WhisperPath,
                whisperArgs,
                progress,
                ParseWhisperProgress,
                cancellationToken).ConfigureAwait(false);

            var generated = whisperPrefix + ".txt";
            if (!File.Exists(generated))
            {
                throw new InvalidOperationException("whisper.cpp n'a pas produit de fichier transcript.");
            }

            var rawText = await File.ReadAllTextAsync(generated, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var text = CleanTranscript(rawText);
            var suspicion = DetectSuspicion(rawText, text, duration);
            await File.WriteAllTextAsync(transcriptPath, text + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TranscriptionProgress("Terminé", 100, "done"));
            return new TranscriptionResult(mediaPath, transcriptPath, text, suspicion.IsSuspicious, suspicion.Reason, "Transcript normal");
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

    private async Task<TranscriptionResult> TranscribeSeparatedSourcesAsync(
        string mixPath,
        string microphonePath,
        string systemPath,
        string language,
        string model,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var transcriptPath = Path.Combine(Path.GetDirectoryName(mixPath) ?? paths.Recordings, "mix.txt");
        var tempDir = Path.Combine(Path.GetTempPath(), "mediascribe-recorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            progress?.Report(new TranscriptionProgress("Préparation des pistes séparées", 0, "prepare"));
            var microphonePrepared = Path.Combine(tempDir, "micro.wav");
            var systemPrepared = Path.Combine(tempDir, "windows.wav");
            var microphoneDuration = await GetMediaDurationAsync(microphonePath, cancellationToken).ConfigureAwait(false);
            var systemDuration = await GetMediaDurationAsync(systemPath, cancellationToken).ConfigureAwait(false);

            await RunProcessAsync(
                FfmpegPath,
                ["-y", "-i", microphonePath, "-progress", "pipe:1", "-nostats", "-vn", "-ac", "1", "-ar", "16000", microphonePrepared],
                progress,
                line => ParseFfmpegProgress(line, microphoneDuration),
                cancellationToken).ConfigureAwait(false);

            await RunProcessAsync(
                FfmpegPath,
                ["-y", "-i", systemPath, "-progress", "pipe:1", "-nostats", "-vn", "-ac", "1", "-ar", "16000", systemPrepared],
                progress,
                line => ParseFfmpegProgress(line, systemDuration),
                cancellationToken).ConfigureAwait(false);

            var microphonePrefix = Path.Combine(tempDir, "micro-transcript");
            var systemPrefix = Path.Combine(tempDir, "windows-transcript");
            progress?.Report(new TranscriptionProgress("Transcription micro", 15, "whisper"));
            await RunWhisperAsync(microphonePrepared, microphonePrefix, language, model, outputJson: true, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TranscriptionProgress("Transcription ordinateur", 55, "whisper"));
            await RunWhisperAsync(systemPrepared, systemPrefix, language, model, outputJson: true, progress, cancellationToken).ConfigureAwait(false);

            var segments = new List<LabeledTranscriptSegment>();
            segments.AddRange(ParseJsonSegments(microphonePrefix + ".json", "Moi"));
            segments.AddRange(ParseJsonSegments(systemPrefix + ".json", "Ordinateur"));
            segments = segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
                .OrderBy(segment => segment.StartSeconds)
                .ThenBy(segment => segment.Speaker)
                .ToList();

            if (segments.Count == 0)
            {
                throw new InvalidOperationException("Aucun segment exploitable dans les pistes séparées.");
            }

            var text = MergeLabeledSegments(segments);
            var suspicion = DetectSuspicion(text, text, Math.Max(microphoneDuration ?? 0, systemDuration ?? 0));
            await File.WriteAllTextAsync(transcriptPath, text + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            progress?.Report(new TranscriptionProgress("Terminé", 100, "done"));
            return new TranscriptionResult(mixPath, transcriptPath, text, suspicion.IsSuspicious, suspicion.Reason, "Avec séparation des sources");
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

    private async Task RunWhisperAsync(
        string preparedWav,
        string outputPrefix,
        string language,
        string model,
        bool outputJson,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var selectedModel = ModelPath(model);
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

        whisperArgs.Add("-sns");
        if (NormalizeModel(model) == "medium")
        {
            whisperArgs.Add("-nf");
        }
        else
        {
            whisperArgs.Add("-lpt");
            whisperArgs.Add("-0.30");
        }

        whisperArgs.Add(outputJson ? "-oj" : "-otxt");
        if (outputJson)
        {
            whisperArgs.Add("-ojf");
        }

        whisperArgs.AddRange(["-of", outputPrefix, "-pp"]);
        await RunProcessAsync(WhisperPath, whisperArgs, progress, ParseWhisperProgress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IProgress<TranscriptionProgress>? progress,
        Func<string, TranscriptionProgress?> parseProgress,
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
            var parsed = parseProgress(e.Data);
            if (parsed is not null)
            {
                progress?.Report(parsed);
            }
            else if (!IsMachineProgressLine(e.Data))
            {
                progress?.Report(new TranscriptionProgress(CompactStatus(e.Data), null, "status"));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            output.AppendLine(e.Data);
            var parsed = parseProgress(e.Data);
            if (parsed is not null)
            {
                progress?.Report(parsed);
            }
            else if (!IsMachineProgressLine(e.Data))
            {
                progress?.Report(new TranscriptionProgress(CompactStatus(e.Data), null, "status"));
            }
        };

        if (!process.Start())
        {
            throw new UserFacingException("TRN-TOOL-002", $"Impossible de démarrer {Path.GetFileName(fileName)}.");
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
            throw new UserFacingException("TRN-TOOL-003", $"{Path.GetFileName(fileName)} a échoué: {Tail(output.ToString(), 2500)}");
        }
    }

    private async Task<double?> GetMediaDurationAsync(string mediaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(FfprobePath))
        {
            return null;
        }

        var output = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = FfprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
        };

        foreach (var argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", mediaPath })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                output.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return null;
        }

        process.BeginOutputReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return double.TryParse(output.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
    }

    private static TranscriptionProgress? ParseFfmpegProgress(string line, double? durationSeconds)
    {
        line = line.Trim();
        if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase)
            && durationSeconds is > 0
            && long.TryParse(line["out_time_ms=".Length..], CultureInfo.InvariantCulture, out var microseconds))
        {
            var processedSeconds = microseconds / 1_000_000d;
            var percent = ClampPercent((int)Math.Round(processedSeconds / durationSeconds.Value * 15d));
            return new TranscriptionProgress($"Préparation audio {percent}%", percent, "prepare");
        }

        if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
        {
            return new TranscriptionProgress("Préparation audio terminée", 15, "prepare");
        }

        return null;
    }

    private static TranscriptionProgress? ParseWhisperProgress(string line)
    {
        var match = Regex.Match(line, @"(?<!\d)(\d{1,3})\s*%");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var rawPercent))
        {
            return null;
        }

        var bounded = Math.Clamp(rawPercent, 0, 100);
        var mapped = ClampPercent(15 + (int)Math.Round(bounded * 0.84d));
        return new TranscriptionProgress($"Transcription {mapped}%", mapped, "whisper");
    }

    private static int ClampPercent(int percent) => Math.Clamp(percent, 0, 100);

    private static string CompactStatus(string line)
    {
        line = line.Trim();
        if (line.Length <= 140) return line;
        return line[..140] + "...";
    }

    private static bool IsMachineProgressLine(string line)
    {
        line = line.Trim();
        return line.Contains('=') && !line.Contains(' ');
    }

    private static string Tail(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[^maxLength..];
    }

    private static string CleanTranscript(string text)
    {
        var lines = text
            .Replace("\r", "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => line != "...")
            .Where(line => !SuspiciousPhrases.Any(blockedText => line.Equals(blockedText, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static TranscriptionSuspicion DetectSuspicion(string rawText, string cleanedText, double? durationSeconds)
    {
        if (SuspiciousPhrases.Any(phrase => rawText.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return new TranscriptionSuspicion(true, "Phrase connue de hallucination Whisper détectée.");
        }

        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            return new TranscriptionSuspicion(true, "Transcript vide après nettoyage.");
        }

        var textLength = cleanedText.Count(char.IsLetterOrDigit);
        if (durationSeconds is >= 12 && textLength < 20)
        {
            return new TranscriptionSuspicion(true, "Transcript très court par rapport à la durée audio.");
        }

        if (durationSeconds is >= 30 && textLength < 60)
        {
            return new TranscriptionSuspicion(true, "Transcript probablement incomplet par rapport à la durée audio.");
        }

        return new TranscriptionSuspicion(false, "");
    }

    private static bool TryGetSeparateTracks(string mediaPath, out string microphonePath, out string systemPath)
    {
        var folder = Path.GetDirectoryName(mediaPath) ?? "";
        var fileName = Path.GetFileName(mediaPath);
        microphonePath = Path.Combine(folder, "micro.wav");
        systemPath = Path.Combine(folder, "windows.wav");
        return fileName.Equals("mix.wav", StringComparison.OrdinalIgnoreCase)
            && File.Exists(microphonePath)
            && File.Exists(systemPath);
    }

    private static IReadOnlyList<LabeledTranscriptSegment> ParseJsonSegments(string jsonPath, string speaker)
    {
        if (!File.Exists(jsonPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var segments = new List<LabeledTranscriptSegment>();
        CollectSegments(document.RootElement, speaker, segments);
        return segments;
    }

    private static void CollectSegments(JsonElement element, string speaker, List<LabeledTranscriptSegment> segments)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryReadSegment(element, speaker, out var segment))
            {
                segments.Add(segment);
                return;
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectSegments(property.Value, speaker, segments);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectSegments(item, speaker, segments);
            }
        }
    }

    private static bool TryReadSegment(JsonElement element, string speaker, out LabeledTranscriptSegment segment)
    {
        segment = default;
        if (!TryGetStringProperty(element, "text", out var text))
        {
            return false;
        }

        text = CleanTranscript(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var start = 0d;
        var end = 0d;
        if (element.TryGetProperty("timestamps", out var timestamps) && timestamps.ValueKind == JsonValueKind.Object)
        {
            if (TryGetStringProperty(timestamps, "from", out var from))
            {
                start = ParseTimestampSeconds(from);
            }

            if (TryGetStringProperty(timestamps, "to", out var to))
            {
                end = ParseTimestampSeconds(to);
            }
        }

        if (start <= 0 && TryGetNumberProperty(element, "start", out var numericStart))
        {
            start = numericStart;
        }

        if (end <= 0 && TryGetNumberProperty(element, "end", out var numericEnd))
        {
            end = numericEnd;
        }

        segment = new LabeledTranscriptSegment(speaker, start, end, text);
        return true;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNumberProperty(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value);
    }

    private static double ParseTimestampSeconds(string value)
    {
        value = value.Trim().Replace(',', '.');
        var parts = value.Split(':');
        if (parts.Length == 3
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
        {
            return hours * 3600 + minutes * 60 + seconds;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawSeconds)
            ? rawSeconds
            : 0;
    }

    private static string MergeLabeledSegments(IReadOnlyList<LabeledTranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        string? activeSpeaker = null;
        foreach (var segment in segments)
        {
            if (!segment.Speaker.Equals(activeSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(segment.Speaker);
                builder.Append(": ");
                builder.Append(segment.Text);
                activeSpeaker = segment.Speaker;
            }
            else
            {
                builder.Append(' ');
                builder.Append(segment.Text);
            }
        }

        return builder.ToString().Trim();
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

public sealed record TranscriptionResult(string MediaPath, string TranscriptPath, string Text, bool IsSuspicious, string SuspicionReason, string TranscriptMode);
public sealed record TranscriptionProgress(string Message, int? Percent, string Stage);
internal sealed record TranscriptionSuspicion(bool IsSuspicious, string Reason);
internal readonly record struct LabeledTranscriptSegment(string Speaker, double StartSeconds, double EndSeconds, string Text);
