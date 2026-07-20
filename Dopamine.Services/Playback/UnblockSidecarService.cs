using Dopamine.Core.Base;
using Dopamine.Core.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class UnblockSidecarService : IUnblockSidecarService
    {
        private const int ProtocolVersion = 1;
        private const int StartupTimeoutSeconds = 8;
        private const int RequestTimeoutSeconds = 20;
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        // Fully qualify: System.Threading and System.Timers both expose Timer.
        private readonly System.Timers.Timer idleTimer;
        private Process process;
        private UnblockProcessJob processJob;
        private HttpClient httpClient;
        private string token;
        private int consecutiveFailures;
        private DateTime circuitOpenUntilUtc;
        private DateTime lastActivityUtc = DateTime.MinValue;
        private UnblockSidecarState state = UnblockSidecarState.Stopped;
        private string version = string.Empty;
        private bool disposed;

        public UnblockSidecarService()
        {
            this.idleTimer = new System.Timers.Timer(30000);
            this.idleTimer.AutoReset = true;
            this.idleTimer.Elapsed += this.IdleTimer_Elapsed;
            this.idleTimer.Start();
        }

        public UnblockSidecarState State => !UnblockNeteaseMusicSettings.IsEnabled
            ? UnblockSidecarState.Disabled
            : this.state;

        public string Version => this.version;

        public event EventHandler StateChanged = delegate { };

        public async Task<UnblockSidecarMatchResult> MatchAsync(
            UnblockSidecarMatchRequest request,
            CancellationToken cancellationToken)
        {
            if (!UnblockNeteaseMusicSettings.IsEnabled)
            {
                return Failure("disabled");
            }

            if (DateTime.UtcNow < this.circuitOpenUntilUtc)
            {
                return Failure("circuit_open");
            }

            if (!await this.EnsureStartedAsync(cancellationToken))
            {
                return Failure("sidecar_unavailable");
            }

            this.TouchActivity();

            await this.requestGate.WaitAsync(cancellationToken);
            try
            {
                string payload = JsonSerializer.Serialize(request, this.jsonOptions);
                using (var message = new HttpRequestMessage(HttpMethod.Post, "v1/match"))
                {
                    message.Headers.Add("X-Dopamine-Sidecar-Token", this.token);
                    message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await this.httpClient.SendAsync(message, cancellationToken))
                    {
                        if (response.Content.Headers.ContentLength.HasValue &&
                            response.Content.Headers.ContentLength.Value > 65536)
                        {
                            return Failure("response_too_large");
                        }

                        string responseText = await ReadLimitedTextAsync(response.Content);

                        UnblockSidecarResponse result = JsonSerializer.Deserialize<UnblockSidecarResponse>(
                            responseText,
                            this.jsonOptions);
                        if (!response.IsSuccessStatusCode || result == null || result.ProtocolVersion != ProtocolVersion ||
                            string.IsNullOrWhiteSpace(result.Url))
                        {
                            return Failure(result?.Error ?? "not_found");
                        }

                        this.consecutiveFailures = 0;
                        return new UnblockSidecarMatchResult
                        {
                            IsSuccess = true,
                            Url = result.Url,
                            Source = result.Source ?? string.Empty,
                            Bitrate = result.Bitrate,
                            Size = result.Size,
                            MediaType = result.MediaType ?? string.Empty
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return Failure("cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Warning("Unblock sidecar request failed. ErrorType={0}", ex.GetType().Name);
                this.RecordFailure();
                return Failure("request_failed");
            }
            finally
            {
                this.requestGate.Release();
            }
        }

        public async Task<bool> RestartAsync(CancellationToken cancellationToken)
        {
            this.Stop();
            this.consecutiveFailures = 0;
            this.circuitOpenUntilUtc = DateTime.MinValue;
            return await this.EnsureStartedAsync(cancellationToken);
        }

        public void Stop()
        {
            this.StopProcess();
            this.SetState(UnblockNeteaseMusicSettings.IsEnabled
                ? UnblockSidecarState.Stopped
                : UnblockSidecarState.Disabled);
        }

        private async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (this.process != null && !this.process.HasExited && this.httpClient != null && this.state == UnblockSidecarState.Ready)
            {
                return true;
            }

            await this.lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (this.process != null && !this.process.HasExited && this.httpClient != null && this.state == UnblockSidecarState.Ready)
                {
                    return true;
                }

                this.StopProcess();
                string executable = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(executable) || !System.IO.File.Exists(executable))
                {
                    this.SetState(UnblockSidecarState.Unavailable);
                    return false;
                }

                this.SetState(UnblockSidecarState.Starting);
                this.token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var ready = new TaskCompletionSource<UnblockSidecarHandshake>();
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.EnvironmentVariables["DOPAMINE_UNBLOCK_TOKEN"] = this.token;
                startInfo.EnvironmentVariables["DOPAMINE_PARENT_PID"] = Process.GetCurrentProcess().Id.ToString();
                startInfo.EnvironmentVariables["ENABLE_FLAC"] = UnblockNeteaseMusicSettings.EnableFlac ? "true" : "false";
                startInfo.EnvironmentVariables["LOG_LEVEL"] = "error";
                startInfo.EnvironmentVariables["JSON_LOG"] = "true";
                startInfo.EnvironmentVariables["FOLLOW_SOURCE_ORDER"] = "true";

                var newProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                newProcess.OutputDataReceived += (_, e) =>
                {
                    const string prefix = "DOPAMINE_UNBLOCK_READY ";
                    if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        try
                        {
                            ready.TrySetResult(JsonSerializer.Deserialize<UnblockSidecarHandshake>(
                                e.Data.Substring(prefix.Length),
                                this.jsonOptions));
                        }
                        catch (Exception ex)
                        {
                            ready.TrySetException(ex);
                        }
                    }
                };
                newProcess.Exited += (_, __) =>
                {
                    if (object.ReferenceEquals(this.process, newProcess))
                    {
                        this.SetState(UnblockSidecarState.Stopped);
                    }
                };

                if (!newProcess.Start())
                {
                    this.SetState(UnblockSidecarState.Unavailable);
                    return false;
                }

                newProcess.BeginOutputReadLine();
                newProcess.BeginErrorReadLine();
                this.process = newProcess;
                try
                {
                    this.processJob = new UnblockProcessJob();
                    this.processJob.Add(newProcess);
                }
                catch (Exception ex)
                {
                    AppLog.Warning("Could not attach Unblock sidecar to a Windows Job Object. ErrorType={0}", ex.GetType().Name);
                    UnblockProcessJob failedJob = Interlocked.Exchange(ref this.processJob, null);
                    failedJob?.Dispose();
                }

                Task completed = await Task.WhenAny(
                    ready.Task,
                    Task.Delay(TimeSpan.FromSeconds(StartupTimeoutSeconds), cancellationToken));
                if (completed != ready.Task)
                {
                    this.StopProcess();
                    this.RecordFailure();
                    return false;
                }

                UnblockSidecarHandshake handshake = await ready.Task;
                if (handshake == null || handshake.ProtocolVersion != ProtocolVersion || handshake.Port < 1)
                {
                    this.StopProcess();
                    this.SetState(UnblockSidecarState.Incompatible);
                    return false;
                }

                this.version = handshake.UnblockVersion ?? string.Empty;
                this.httpClient = new HttpClient
                {
                    BaseAddress = new Uri("http://127.0.0.1:" + handshake.Port + "/"),
                    Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
                };
                this.consecutiveFailures = 0;
                this.TouchActivity();
                this.SetState(UnblockSidecarState.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                this.StopProcess();
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not start Unblock sidecar. ErrorType={0}", ex.GetType().Name);
                this.StopProcess();
                this.RecordFailure();
                return false;
            }
            finally
            {
                this.lifecycleGate.Release();
            }
        }

        private void TouchActivity()
        {
            this.lastActivityUtc = DateTime.UtcNow;
        }

        private void IdleTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (this.disposed || this.process == null)
                {
                    return;
                }

                if (this.lastActivityUtc == DateTime.MinValue)
                {
                    return;
                }

                TimeSpan idle = DateTime.UtcNow - this.lastActivityUtc;
                if (idle.TotalMinutes < Constants.UnblockSidecarIdleTimeoutMinutes)
                {
                    return;
                }

                AppLog.Info("Stopping idle Unblock sidecar after {0} minutes without requests.",
                    Constants.UnblockSidecarIdleTimeoutMinutes);
                this.Stop();
            }
            catch (Exception ex)
            {
                AppLog.Warning("Unblock sidecar idle check failed. ErrorType={0}", ex.GetType().Name);
            }
        }

        private static string GetExecutablePath()
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                return string.Empty;
            }

            string architecture = string.Equals(
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"),
                "ARM64",
                StringComparison.OrdinalIgnoreCase)
                ? "win-arm64"
                : "win-x64";
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnblockNeteaseMusic",
                architecture,
                "Dopamine.UnblockSidecar.exe");
        }

        private void RecordFailure()
        {
            this.consecutiveFailures++;
            if (this.consecutiveFailures >= 3)
            {
                this.circuitOpenUntilUtc = DateTime.UtcNow.AddMinutes(5);
            }
            this.SetState(UnblockSidecarState.Unavailable);
        }

        private void StopProcess()
        {
            HttpClient client = Interlocked.Exchange(ref this.httpClient, null);
            client?.Dispose();
            this.version = string.Empty;
            Process oldProcess = Interlocked.Exchange(ref this.process, null);
            UnblockProcessJob oldJob = Interlocked.Exchange(ref this.processJob, null);
            oldJob?.Dispose();

            if (oldProcess != null)
            {
                try
                {
                    if (!oldProcess.HasExited)
                    {
                        oldProcess.Kill();
                        oldProcess.WaitForExit(3000);
                    }
                }
                catch
                {
                }
                oldProcess.Dispose();
            }
        }

        private void SetState(UnblockSidecarState value)
        {
            if (this.state != value)
            {
                this.state = value;
                this.StateChanged(this, EventArgs.Empty);
            }
        }

        private static UnblockSidecarMatchResult Failure(string code)
        {
            return new UnblockSidecarMatchResult { ErrorCode = code ?? "unknown" };
        }

        private static async Task<string> ReadLimitedTextAsync(HttpContent content)
        {
            using (Stream stream = await content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
            {
                var output = new StringBuilder();
                var buffer = new char[4096];
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    output.Append(buffer, 0, count);
                    if (output.Length > 65536)
                    {
                        throw new InvalidDataException("Unblock sidecar response exceeded the size limit.");
                    }
                }

                return output.ToString();
            }
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            try
            {
                this.idleTimer.Stop();
                this.idleTimer.Dispose();
            }
            catch
            {
            }

            this.StopProcess();
            this.lifecycleGate.Dispose();
            this.requestGate.Dispose();
        }
    }
}
