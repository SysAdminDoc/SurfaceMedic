using System.Text;
using SurfaceMedic.Core.Models;

namespace SurfaceMedic.Core.Infrastructure;

internal sealed class PowerShellAdapter(ProcessRunner processRunner)
{
    public Task<ProcessResult> RunAsync(
        string script,
        string operation,
        OperationCallbacks? callbacks,
        bool streamOutput,
        CancellationToken cancellationToken)
    {
        var wrappedScript = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); " +
                            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
                            script;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedScript));
        return processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodedCommand],
            operation,
            callbacks,
            streamOutput,
            filterProgressNoise: true,
            commandDescription: "> powershell.exe -NoProfile -NonInteractive (script)",
            cancellationToken);
    }
}
