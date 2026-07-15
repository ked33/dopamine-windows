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
    public sealed class DpapiNeteaseRecommendationStore : INeteaseRecommendationStore
    {
        private const int CurrentVersion = 1;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dopamine.Netease.DailyRecommendations.v1");

        private readonly string directory;
        private readonly string path;
        private readonly string temporaryPath;

        public DpapiNeteaseRecommendationStore()
            : this(SettingsClient.ApplicationFolder())
        {
        }

        public DpapiNeteaseRecommendationStore(string applicationFolder)
        {
            this.directory = Path.Combine(applicationFolder, "Netease");
            this.path = Path.Combine(this.directory, "daily-recommendations.dat");
            this.temporaryPath = this.path + ".tmp";
        }

        public Task<NeteaseRecommendationLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (!System.IO.File.Exists(this.path))
                {
                    return new NeteaseRecommendationLoadResult { Exists = false, IsSuccess = true };
                }

                byte[] plainBytes = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] protectedBytes = System.IO.File.ReadAllBytes(this.path);
                    plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    var snapshot = JsonConvert.DeserializeObject<NeteaseRecommendationSnapshot>(
                        Encoding.UTF8.GetString(plainBytes));

                    if (snapshot == null || snapshot.Version != CurrentVersion ||
                        snapshot.Songs == null || string.IsNullOrWhiteSpace(snapshot.AccountUserId))
                    {
                        return Failure();
                    }

                    return new NeteaseRecommendationLoadResult
                    {
                        Exists = true,
                        IsSuccess = true,
                        Snapshot = snapshot
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLog.Warning(
                        "Could not restore the encrypted Netease recommendation cache. ErrorType={0}",
                        ex.GetType().Name);
                    return Failure();
                }
                finally
                {
                    ClearBytes(plainBytes);
                }
            }, cancellationToken);
        }

        public Task<NeteaseResult<bool>> SaveAsync(
            NeteaseRecommendationSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                byte[] plainBytes = null;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(this.directory);
                    snapshot.Version = CurrentVersion;
                    plainBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot, Formatting.None));
                    byte[] protectedBytes = ProtectedData.Protect(
                        plainBytes,
                        Entropy,
                        DataProtectionScope.CurrentUser);

                    using (var stream = new FileStream(
                        this.temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        stream.Write(protectedBytes, 0, protectedBytes.Length);
                        stream.Flush(true);
                    }

                    if (System.IO.File.Exists(this.path))
                    {
                        System.IO.File.Replace(this.temporaryPath, this.path, null);
                    }
                    else
                    {
                        System.IO.File.Move(this.temporaryPath, this.path);
                    }

                    return NeteaseResult<bool>.Success(true);
                }
                catch (OperationCanceledException)
                {
                    TryDelete(this.temporaryPath);
                    throw;
                }
                catch (Exception ex)
                {
                    TryDelete(this.temporaryPath);
                    AppLog.Warning(
                        "Could not persist the encrypted Netease recommendation cache. ErrorType={0}",
                        ex.GetType().Name);
                    return NeteaseResult<bool>.Failure(new NeteaseError(
                        NeteaseErrorCode.StorageFailed,
                        "Language_Netease_Service_Unavailable"));
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
                TryDelete(this.path);
                TryDelete(this.temporaryPath);
            });
        }

        private static NeteaseRecommendationLoadResult Failure()
        {
            return new NeteaseRecommendationLoadResult
            {
                Exists = true,
                IsSuccess = false,
                Error = new NeteaseError(
                    NeteaseErrorCode.StorageFailed,
                    "Language_Netease_Service_Unavailable")
            };
        }

        private static void ClearBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not delete a Netease recommendation cache file. ErrorType={0}",
                    ex.GetType().Name);
            }
        }
    }
}
