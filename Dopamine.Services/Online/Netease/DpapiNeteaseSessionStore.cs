using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class DpapiNeteaseSessionStore : INeteaseSessionStore
    {
        private const int CurrentVersion = 1;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dopamine.Netease.Session.v1");

        private readonly string sessionDirectory;
        private readonly string sessionPath;
        private readonly string temporaryPath;

        public DpapiNeteaseSessionStore()
            : this(SettingsClient.ApplicationFolder())
        {
        }

        public DpapiNeteaseSessionStore(string applicationFolder)
        {
            this.sessionDirectory = Path.Combine(applicationFolder, "Netease");
            this.sessionPath = Path.Combine(this.sessionDirectory, "session.dat");
            this.temporaryPath = this.sessionPath + ".tmp";
        }

        public Task<NeteaseSessionLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(this.sessionPath))
                {
                    return new NeteaseSessionLoadResult { Exists = false, IsSuccess = true };
                }

                byte[] plainBytes = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] protectedBytes = File.ReadAllBytes(this.sessionPath);
                    plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    string json = Encoding.UTF8.GetString(plainBytes);
                    var snapshot = JsonConvert.DeserializeObject<NeteaseSessionSnapshot>(json);

                    if (snapshot == null || snapshot.Version != CurrentVersion || snapshot.Cookies == null || snapshot.Cookies.Count == 0)
                    {
                        return Failure(NeteaseErrorCode.StorageFailed);
                    }

                    return new NeteaseSessionLoadResult
                    {
                        Exists = true,
                        IsSuccess = true,
                        Snapshot = snapshot
                    };
                }
                catch (OperationCanceledException)
                {
                    return Failure(NeteaseErrorCode.Cancelled);
                }
                catch (Exception ex)
                {
                    AppLog.Warning("Could not restore the encrypted Netease session. ErrorType={0}", ex.GetType().Name);
                    return Failure(NeteaseErrorCode.StorageFailed);
                }
                finally
                {
                    ClearBytes(plainBytes);
                }
            }, cancellationToken);
        }

        public Task<NeteaseResult<bool>> SaveAsync(NeteaseSessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                byte[] plainBytes = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(this.sessionDirectory);

                    snapshot.Version = CurrentVersion;
                    string json = JsonConvert.SerializeObject(snapshot, Formatting.None);
                    plainBytes = Encoding.UTF8.GetBytes(json);
                    byte[] protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

                    using (var stream = new FileStream(this.temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(protectedBytes, 0, protectedBytes.Length);
                        stream.Flush(true);
                    }

                    if (File.Exists(this.sessionPath))
                    {
                        File.Replace(this.temporaryPath, this.sessionPath, null);
                    }
                    else
                    {
                        File.Move(this.temporaryPath, this.sessionPath);
                    }

                    return NeteaseResult<bool>.Success(true);
                }
                catch (OperationCanceledException)
                {
                    TryDelete(this.temporaryPath);
                    return NeteaseResult<bool>.Failure(new NeteaseError(
                        NeteaseErrorCode.Cancelled,
                        "Language_Netease_Cancelled"));
                }
                catch (Exception ex)
                {
                    TryDelete(this.temporaryPath);
                    AppLog.Warning("Could not persist the encrypted Netease session. ErrorType={0}", ex.GetType().Name);
                    return NeteaseResult<bool>.Failure(new NeteaseError(
                        NeteaseErrorCode.StorageFailed,
                        "Language_Netease_Session_Save_Failed"));
                }
                finally
                {
                    ClearBytes(plainBytes);
                }
            }, cancellationToken);
        }

        public Task DeleteAsync()
        {
            return Task.Run(() =>
            {
                TryDelete(this.sessionPath);
                TryDelete(this.temporaryPath);
            });
        }

        private static NeteaseSessionLoadResult Failure(NeteaseErrorCode code)
        {
            return new NeteaseSessionLoadResult
            {
                Exists = true,
                IsSuccess = false,
                Error = new NeteaseError(code, code == NeteaseErrorCode.Cancelled
                    ? "Language_Netease_Cancelled"
                    : "Language_Netease_Session_Restore_Failed")
            };
        }

        private static void ClearBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not delete a Netease session file. ErrorType={0}", ex.GetType().Name);
            }
        }
    }
}
