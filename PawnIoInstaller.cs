using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HotCPU
{
    /// <summary>
    /// Result of a PawnIO download/install attempt.
    /// </summary>
    internal enum PawnIoInstallOutcome
    {
        Installed,
        UserCancelled,
        InstallerFailed,
        DownloadFailed,
        HashMismatch,
        NotAvailable,
    }

    /// <summary>
    /// Detailed result from <see cref="PawnIoInstaller.DownloadAndRunAsync"/>.
    /// Carries the installer exit code so the caller can surface a meaningful
    /// error message instead of a generic "installer reported an error".
    /// </summary>
    internal record PawnIoInstallResult(PawnIoInstallOutcome Outcome, int? ExitCode = null, string? Detail = null);

    /// <summary>
    /// Fetches the official PawnIO installer from GitHub, validates its
    /// SHA-256, and launches it. We deliberately do NOT bundle the signed
    /// setup.exe in our source tree / output:
    ///   * PawnIO is GPL v2. Shipping the binary would drag redistribution
    ///     obligations onto HotCPU. Communicating with the already-installed
    ///     driver through its IOCTL interface (which LibreHardwareMonitorLib
    ///     does for us) is expressly exempted by PawnIO's licence.
    ///   * Downloading on demand guarantees the user always gets the
    ///     latest signed release.
    /// </summary>
    internal static class PawnIoInstaller
    {
        private const string ReleaseApiUrl = "https://api.github.com/repos/namazso/PawnIO.Setup/releases/latest";

        // Hard cap: the real installer is ~3.4 MB. Anything an order of
        // magnitude larger is suspicious and we refuse to execute it.
        private const long MaxInstallerBytes = 32L * 1024 * 1024;

        // PawnIO.Setup is NOT a classic NSIS installer - it exposes its own
        // argument parser. "-install" runs the install action; "-silent"
        // suppresses all UI; "-unrestricted" installs the edition that
        // allows non-administrator processes (like HotCPU running as
        // asInvoker) to issue LoadBinary ioctls on \\.\PawnIO.
        //
        // Without -unrestricted, LibreHardwareMonitorLib opens the device
        // handle fine but its LoadBinary call silently returns
        // ACCESS_DENIED, so Tctl/Tdie/CCD temps and package power all read 0.
        // (Running with "/S" returns "Unknown argument: /S".)
        private const string InstallArg = "-install";
        private const string SilentArg = "-silent";
        private const string UnrestrictedArg = "-unrestricted";

        /// <summary>
        /// Download and launch the PawnIO installer. Returns an outcome the
        /// caller can surface to the user. This method is safe to call on the
        /// UI thread (it does all I/O async).
        /// </summary>
        public static async Task<PawnIoInstallResult> DownloadAndRunAsync(
            bool silent,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"HotCPU_PawnIO_setup_{Guid.NewGuid():N}.exe");

            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
                // GitHub API requires a User-Agent.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("HotCPU/1.0 (+https://github.com/)");
                http.Timeout = TimeSpan.FromMinutes(2);

                // 1. Resolve the latest release metadata.
                var (downloadUrl, expectedSha256, expectedSize) = await ResolveLatestAssetAsync(http, cancellationToken);
                if (string.IsNullOrEmpty(downloadUrl))
                    return new PawnIoInstallResult(PawnIoInstallOutcome.NotAvailable);

                // 2. Download to temp with progress + byte cap.
                using (var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    resp.EnsureSuccessStatusCode();

                    long? contentLength = resp.Content.Headers.ContentLength;
                    if (contentLength is > MaxInstallerBytes)
                        return new PawnIoInstallResult(PawnIoInstallOutcome.DownloadFailed, Detail: "Download size exceeds cap");

                    using var netStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await netStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        totalRead += read;
                        if (totalRead > MaxInstallerBytes)
                            return new PawnIoInstallResult(PawnIoInstallOutcome.DownloadFailed, Detail: "Download exceeded cap mid-stream");

                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                        if (contentLength is > 0)
                            progress?.Report((double)totalRead / contentLength.Value);
                    }
                }

                // 3. Verify SHA-256 against the GitHub release digest. If
                // digest wasn't published for some reason, fall back to a
                // size sanity check: it's still better than blind execution.
                if (!string.IsNullOrEmpty(expectedSha256))
                {
                    var actual = ComputeSha256(tempPath);
                    if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        return new PawnIoInstallResult(PawnIoInstallOutcome.HashMismatch);
                }
                else if (expectedSize > 0)
                {
                    var actualSize = new FileInfo(tempPath).Length;
                    if (actualSize != expectedSize)
                        return new PawnIoInstallResult(PawnIoInstallOutcome.DownloadFailed, Detail: "File size mismatch");
                }

                // 4. Launch the installer. UseShellExecute=true is what
                // surfaces the UAC elevation prompt; the installer itself
                // asks for admin rights via its manifest.
                var psi = new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                    Verb = "runas",
                };
                // Always pass -install so the setup binary actually runs the
                // install action instead of bouncing into its interactive
                // launcher. -silent additionally suppresses the setup UI.
                // -unrestricted exposes the driver to non-admin callers,
                // without it HotCPU (asInvoker) can't make LHM talk to the
                // PawnIO driver.
                var parts = new System.Collections.Generic.List<string> { InstallArg, UnrestrictedArg };
                if (silent) parts.Add(SilentArg);
                psi.Arguments = string.Join(' ', parts);

                Process? proc;
                try
                {
                    proc = Process.Start(psi);
                }
                catch (System.ComponentModel.Win32Exception ex) when (unchecked((uint)ex.NativeErrorCode) == 0x800704C7u || ex.NativeErrorCode == 1223)
                {
                    // User rejected the UAC prompt.
                    return new PawnIoInstallResult(PawnIoInstallOutcome.UserCancelled);
                }

                if (proc == null)
                    return new PawnIoInstallResult(PawnIoInstallOutcome.InstallerFailed, Detail: "Process.Start returned null");

                await proc.WaitForExitAsync(cancellationToken);

                // PawnIO.Setup 2.2.0 returns DOS error codes (0 = success,
                // ERROR_SUCCESS_REBOOT_REQUIRED = 3010 means success-pending-reboot).
                // Anything else means the installer rejected the request.
                const int ErrorSuccessRebootRequired = 3010;
                int exit = proc.ExitCode;
                if (exit != 0 && exit != ErrorSuccessRebootRequired)
                    return new PawnIoInstallResult(PawnIoInstallOutcome.InstallerFailed, ExitCode: exit);

                // Re-check driver registration. If the installer succeeded
                // synchronously this will flip us to Installed; if a reboot
                // is pending we still report success so the caller can
                // prompt the user appropriately.
                return CpuDriverHelper.IsPawnIoInstalled()
                    ? new PawnIoInstallResult(PawnIoInstallOutcome.Installed, ExitCode: exit)
                    : new PawnIoInstallResult(PawnIoInstallOutcome.InstallerFailed, ExitCode: exit, Detail: "Installer exited 0 but PawnIO still not detected");
            }
            catch (OperationCanceledException)
            {
                return new PawnIoInstallResult(PawnIoInstallOutcome.UserCancelled);
            }
            catch (Exception ex)
            {
                return new PawnIoInstallResult(PawnIoInstallOutcome.DownloadFailed, Detail: ex.Message);
            }
            finally
            {
                // Best-effort cleanup of the downloaded installer. If
                // the process we spawned still has a handle on it, we
                // leave the file for the OS to clean up from %TEMP%.
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { /* ignore */ }
            }
        }

        private static async Task<(string? url, string? sha256, long size)> ResolveLatestAssetAsync(HttpClient http, CancellationToken ct)
        {
            try
            {
                using var resp = await http.GetAsync(ReleaseApiUrl, ct);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (!doc.RootElement.TryGetProperty("assets", out var assets)) return default;

                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameProp)) continue;
                    var name = nameProp.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    // We only trust the NSIS installer published by the author.
                    if (!name.EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.StartsWith("PawnIO", StringComparison.OrdinalIgnoreCase)) continue;

                    string? url = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
                    long size = asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var s) ? s : 0;

                    string? sha = null;
                    if (asset.TryGetProperty("digest", out var digestProp))
                    {
                        var digest = digestProp.GetString();
                        // GitHub returns "sha256:<hex>". Strip the algorithm prefix.
                        if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            sha = digest.Substring("sha256:".Length);
                    }

                    return (url, sha, size);
                }
            }
            catch
            {
                // Network / JSON error - caller treats as NotAvailable.
            }

            return default;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash);
        }
    }
}
