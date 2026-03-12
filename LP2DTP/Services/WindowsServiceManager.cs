using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LP2DTP.Services
{
    public sealed class WindowsServiceManager
    {
        private static readonly Regex StateRegex = new(@"STATE\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public const string DefaultServiceName = "LP2SVR";
        public const string DefaultDisplayName = "LP2SVR";
        public const string DefaultDescription = "LP2 polling service.";

        public bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public string? TryResolveExecutablePath(string? providedPath = null)
        {
            if (!string.IsNullOrWhiteSpace(providedPath) && File.Exists(providedPath))
            {
                return Path.GetFullPath(providedPath);
            }

            foreach (var root in EnumerateSearchRoots())
            {
                foreach (var candidate in BuildCandidatePaths(root))
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }

            return null;
        }

        public async Task RegisterAsync(
            string executablePath,
            string? serviceName = null,
            string? displayName = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("LP2SVR.exe path is required.");
            }

            var normalizedServiceName = NormalizeServiceName(serviceName);
            var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedServiceName : displayName.Trim();
            var normalizedDescription = string.IsNullOrWhiteSpace(description) ? DefaultDescription : description.Trim();

            var fullPath = Path.GetFullPath(executablePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("LP2SVR.exe was not found.", fullPath);
            }

            var binaryPath = BuildServiceBinaryPath(fullPath, normalizedServiceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);

            if (state == ServiceState.NotInstalled)
            {
                await InvokeScAsync(new[] { "create", normalizedServiceName, "binPath=", binaryPath, "start=", "auto", "DisplayName=", normalizedDisplayName }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (state != ServiceState.Stopped)
                {
                    await StopAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
                }

                await InvokeScAsync(new[] { "config", normalizedServiceName, "binPath=", binaryPath, "start=", "auto", "DisplayName=", normalizedDisplayName }, cancellationToken).ConfigureAwait(false);
            }

            await InvokeScAsync(new[] { "description", normalizedServiceName, normalizedDescription }, cancellationToken).ConfigureAwait(false);
            await InvokeScAsync(new[] { "failure", normalizedServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000" }, cancellationToken).ConfigureAwait(false);
            await InvokeScAsync(new[] { "failureflag", normalizedServiceName, "1" }, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();

            var normalizedServiceName = NormalizeServiceName(serviceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            if (state == ServiceState.NotInstalled)
            {
                return;
            }

            if (state != ServiceState.Stopped)
            {
                await StopAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            }

            await InvokeScAsync(new[] { "delete", normalizedServiceName }, cancellationToken).ConfigureAwait(false);
        }

        public async Task StartAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();

            var normalizedServiceName = NormalizeServiceName(serviceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            if (state == ServiceState.NotInstalled)
            {
                throw new InvalidOperationException("Service is not installed.");
            }

            if (state == ServiceState.Running)
            {
                return;
            }

            await InvokeScAsync(new[] { "start", normalizedServiceName }, cancellationToken).ConfigureAwait(false);
            await WaitForStateAsync(normalizedServiceName, ServiceState.Running, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();

            var normalizedServiceName = NormalizeServiceName(serviceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            if (state == ServiceState.NotInstalled || state == ServiceState.Stopped)
            {
                return;
            }

            await InvokeScAsync(new[] { "stop", normalizedServiceName }, cancellationToken).ConfigureAwait(false);
            await WaitForStateAsync(normalizedServiceName, ServiceState.Stopped, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }

        public async Task RestartAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();
            var normalizedServiceName = NormalizeServiceName(serviceName);
            await StopAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            await StartAsync(normalizedServiceName, cancellationToken). ConfigureAwait(false);
        }

        public async Task<string> GetStatusTextAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            return state switch
            {
                ServiceState.NotInstalled => "Not installed",
                ServiceState.Stopped => "Stopped",
                ServiceState.StartPending => "Start pending",
                ServiceState.StopPending => "Stop pending",
                ServiceState.Running => "Running",
                ServiceState.ContinuePending => "Continue pending",
                ServiceState.PausePending => "Pause pending",
                ServiceState.Paused => "Paused",
                _ => "Unknown"
            };
        }

        public async Task<string?> GetDescriptionAsync(string? serviceName = null, CancellationToken cancellationToken = default)
        {
            var normalizedServiceName = NormalizeServiceName(serviceName);
            var state = await GetStateAsync(normalizedServiceName, cancellationToken).ConfigureAwait(false);
            if (state == ServiceState.NotInstalled)
            {
                return null;
            }

            var result = await InvokeScAsync(new[] { "qdescription", normalizedServiceName }, cancellationToken, allowExitCodes: new[] { 0, 1060 }).ConfigureAwait(false);
            if (result.ExitCode == 1060)
            {
                return null;
            }

            return ParseServiceDescription(result.Output);
        }

        private static string[] BuildCandidatePaths(string root)
        {
            return new[]
            {
                Path.Combine(root, "LP2SVR", "bin", "Release", "net10.0-windows", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "Debug", "net10.0-windows", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "x64", "Release", "net10.0-windows", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "x64", "Debug", "net10.0-windows", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "Release", "net10.0-windows", "publish", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "Debug", "net10.0-windows", "publish", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "x64", "Release", "net10.0-windows", "publish", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR", "bin", "x64", "Debug", "net10.0-windows", "publish", "LP2SVR.exe"),
                Path.Combine(root, "LP2SVR.exe")
            };
        }

        private static string[] EnumerateSearchRoots()
        {
            var roots = new System.Collections.Generic.List<string>();
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                roots.Add(current.FullName);
                current = current.Parent;
            }

            return roots.ToArray();
        }

        private async Task WaitForStateAsync(string serviceName, ServiceState expectedState, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var currentState = await GetStateAsync(serviceName, cancellationToken).ConfigureAwait(false);
                if (currentState == expectedState)
                {
                    return;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for service state '{expectedState}'.");
        }

        private async Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken)
        {
            var result = await InvokeScAsync(new[] { "query", serviceName }, cancellationToken, allowExitCodes: new[] { 0, 1060 }).ConfigureAwait(false);
            if (result.ExitCode == 1060)
            {
                return ServiceState.NotInstalled;
            }

            var match = StateRegex.Match(result.Output);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var stateValue))
            {
                return ServiceState.Unknown;
            }

            return Enum.IsDefined(typeof(ServiceState), stateValue)
                ? (ServiceState)stateValue
                : ServiceState.Unknown;
        }

        private static string NormalizeServiceName(string? serviceName)
        {
            return string.IsNullOrWhiteSpace(serviceName)
                ? DefaultServiceName
                : serviceName.Trim();
        }

        private static string BuildServiceBinaryPath(string fullExecutablePath, string serviceName)
        {
            var escapedServiceName = serviceName.Replace("\"", "\\\"");
            return $"\"{fullExecutablePath}\" --ServiceName \"{escapedServiceName}\"";
        }

        private void EnsureAdministrator()
        {
            if (!IsAdministrator())
            {
                throw new InvalidOperationException("Run LP2DTP as Administrator.");
            }
        }

        private static async Task<ScResult> InvokeScAsync(string[] arguments, CancellationToken cancellationToken, int[]? allowExitCodes = null)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            var output = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput
                : $"{standardOutput}{Environment.NewLine}{standardError}";

            if (allowExitCodes == null || Array.IndexOf(allowExitCodes, process.ExitCode) < 0)
            {
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"sc.exe failed ({process.ExitCode}): {output}".Trim());
                }
            }

            return new ScResult(process.ExitCode, output.Trim());
        }

        private readonly record struct ScResult(int ExitCode, string Output);

        private enum ServiceState
        {
            Unknown = -1,
            NotInstalled = 0,
            Stopped = 1,
            StartPending = 2,
            StopPending = 3,
            Running = 4,
            ContinuePending = 5,
            PausePending = 6,
            Paused = 7
        }

        private static string ParseServiceDescription(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var descriptionParts = new System.Collections.Generic.List<string>();
            var capture = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    if (capture)
                    {
                        break;
                    }

                    continue;
                }

                if (trimmed.StartsWith("[SC]", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("SERVICE_NAME", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!capture)
                {
                    var separatorIndex = line.IndexOf(':');
                    if (separatorIndex < 0)
                    {
                        continue;
                    }

                    var left = line[..separatorIndex].Trim();
                    if (left.Length == 0)
                    {
                        continue;
                    }

                    var firstPart = line[(separatorIndex + 1)..].Trim();
                    if (firstPart.Length > 0)
                    {
                        descriptionParts.Add(firstPart);
                    }

                    capture = true;
                    continue;
                }

                if (!char.IsWhiteSpace(line[0]) && line.Contains(':'))
                {
                    break;
                }

                descriptionParts.Add(trimmed);
            }

            return string.Join(" ", descriptionParts).Trim();
        }
    }
}
