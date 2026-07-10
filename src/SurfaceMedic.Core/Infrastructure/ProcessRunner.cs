using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SurfaceMedic.Core.Models;

namespace SurfaceMedic.Core.Infrastructure;

internal sealed class ProcessRunner
{
    private static readonly Regex PercentPattern = new(@"(?<!\d)(?<percent>100|[1-9]?\d)%", RegexOptions.Compiled);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string operation,
        OperationCallbacks? callbacks,
        bool streamOutput,
        bool filterProgressNoise,
        string? commandDescription,
        CancellationToken cancellationToken)
    {
        var argumentList = arguments.ToArray();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ReportLog(
            callbacks,
            operation,
            LogLevel.Command,
            commandDescription ?? $"> {fileName} {FormatArguments(argumentList)}");
        ReportProgress(callbacks, operation, "Starting operation", null);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Windows could not start {fileName}.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"{fileName} was not found or could not be started.", exception);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = PumpAsync(
            process.StandardOutput,
            stdout,
            operation,
            callbacks,
            LogLevel.Output,
            streamOutput,
            filterProgressNoise,
            cancellationToken);
        var stderrTask = PumpAsync(
            process.StandardError,
            stderr,
            operation,
            callbacks,
            LogLevel.Error,
            streamOutput,
            false,
            cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAfterCancellationAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            ReportLog(callbacks, operation, LogLevel.Warning, "Operation cancelled.");
            throw;
        }

        var result = new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        var completionMessage = result.Succeeded
            ? "Operation completed"
            : $"Operation exited with code {result.ExitCode}";
        ReportProgress(callbacks, operation, completionMessage, result.Succeeded ? 100 : null);
        ReportLog(
            callbacks,
            operation,
            result.Succeeded ? LogLevel.Information : LogLevel.Error,
            completionMessage + ".");
        return result;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder destination,
        string operation,
        OperationCallbacks? callbacks,
        LogLevel level,
        bool reportLines,
        bool filterProgressNoise,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            line = line.Replace("\0", string.Empty).Replace("\b", string.Empty).TrimEnd();
            destination.AppendLine(line);

            var percentMatch = PercentPattern.Match(line);
            if (percentMatch.Success && int.TryParse(percentMatch.Groups["percent"].Value, out var percent))
            {
                ReportProgress(callbacks, operation, "Working", percent);
            }

            if (reportLines && !string.IsNullOrWhiteSpace(line) &&
                (!filterProgressNoise || !LooksLikeProgressNoise(line)))
            {
                ReportLog(callbacks, operation, level, line);
            }
        }
    }

    private static bool LooksLikeProgressNoise(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Contains('%') && (trimmed.StartsWith('[') || trimmed.Contains(" / ")))
        {
            return true;
        }

        return trimmed.All(character => char.IsWhiteSpace(character) || "-|/\\[]#=".Contains(character));
    }

    private static string FormatArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(argument =>
            argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument));

    private static void ReportProgress(
        OperationCallbacks? callbacks,
        string operation,
        string message,
        int? percent)
    {
        try
        {
            callbacks?.Progress?.Report(new OperationProgress(operation, message, percent, percent is null));
        }
        catch
        {
            // Consumer callbacks must not terminate a system operation.
        }
    }

    internal static void ReportLog(
        OperationCallbacks? callbacks,
        string operation,
        LogLevel level,
        string message)
    {
        try
        {
            callbacks?.Log?.Report(new LogEntry(DateTimeOffset.Now, level, operation, message));
        }
        catch
        {
            // Consumer callbacks must not terminate a system operation.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static async Task DrainAfterCancellationAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
