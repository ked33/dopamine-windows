using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteaseSessionService : INeteaseSessionService
    {
        private readonly INeteaseApiClient apiClient;
        private readonly INeteaseSessionStore sessionStore;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);

        private NeteaseSessionState state = NeteaseSessionState.SignedOut;
        private NeteaseAccountProfile account;
        private long sessionGeneration;
        private long loginGeneration;

        public NeteaseSessionState State => this.state;

        public NeteaseAccountProfile Account => this.account;

        public long SessionGeneration => Interlocked.Read(ref this.sessionGeneration);

        public event EventHandler SessionChanged = delegate { };

        public NeteaseSessionService(INeteaseApiClient apiClient, INeteaseSessionStore sessionStore)
        {
            this.apiClient = apiClient;
            this.sessionStore = sessionStore;
        }

        public async Task RestoreAsync(CancellationToken cancellationToken)
        {
            await this.operationGate.WaitAsync(cancellationToken);

            try
            {
                this.SetState(NeteaseSessionState.Restoring, null);
                NeteaseSessionLoadResult loadResult = await this.sessionStore.LoadAsync(cancellationToken);

                if (!loadResult.Exists)
                {
                    this.apiClient.ClearCookies();
                    this.SetState(NeteaseSessionState.SignedOut, null);
                    return;
                }

                if (!loadResult.IsSuccess || loadResult.Snapshot == null)
                {
                    this.apiClient.ClearCookies();
                    this.SetState(NeteaseSessionState.SignedOut, null);
                    return;
                }

                this.apiClient.ReplaceCookies(loadResult.Snapshot.Cookies);
                NeteaseResult<NeteaseAccountProfile> status = await this.apiClient.GetLoginStatusAsync(cancellationToken);

                if (status.IsSuccess)
                {
                    Interlocked.Increment(ref this.sessionGeneration);
                    this.SetState(NeteaseSessionState.SignedIn, status.Value);
                    return;
                }

                if (status.Error != null && status.Error.Code == NeteaseErrorCode.NetworkUnavailable)
                {
                    this.SetState(NeteaseSessionState.OfflineUnknown, loadResult.Snapshot.Account);
                    return;
                }

                if (IsAuthenticationError(status.Error))
                {
                    await this.sessionStore.DeleteAsync();
                    this.apiClient.ClearCookies();
                    Interlocked.Increment(ref this.sessionGeneration);
                    this.SetState(NeteaseSessionState.Expired, null);
                    return;
                }

                this.SetState(NeteaseSessionState.Error, null);
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        public async Task<NeteaseResult<NeteaseQrSession>> BeginQrLoginAsync(CancellationToken cancellationToken)
        {
            await this.operationGate.WaitAsync(cancellationToken);

            try
            {
                long generation = Interlocked.Increment(ref this.loginGeneration);
                this.apiClient.ClearCookies();
                this.SetState(NeteaseSessionState.SigningIn, null);

                NeteaseResult<NeteaseQrKey> result = await this.apiClient.CreateQrKeyAsync(cancellationToken);

                if (!result.IsSuccess)
                {
                    this.SetState(result.Error != null && result.Error.Code == NeteaseErrorCode.Cancelled
                        ? NeteaseSessionState.SignedOut
                        : NeteaseSessionState.Error, null);
                    return NeteaseResult<NeteaseQrSession>.Failure(result.Error);
                }

                return NeteaseResult<NeteaseQrSession>.Success(new NeteaseQrSession
                {
                    Unikey = result.Value.Unikey,
                    LoginGeneration = generation
                });
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        public async Task<NeteaseQrPollResult> PollQrLoginAsync(NeteaseQrSession session, CancellationToken cancellationToken)
        {
            if (session == null || session.LoginGeneration != Interlocked.Read(ref this.loginGeneration))
            {
                return new NeteaseQrPollResult { State = NeteaseQrState.Cancelled };
            }

            await this.operationGate.WaitAsync(cancellationToken);

            try
            {
                if (session.LoginGeneration != Interlocked.Read(ref this.loginGeneration))
                {
                    return new NeteaseQrPollResult { State = NeteaseQrState.Cancelled };
                }

                NeteaseResult<NeteaseQrCheck> result = await this.apiClient.CheckQrAsync(session.Unikey, cancellationToken);

                if (!result.IsSuccess)
                {
                    return new NeteaseQrPollResult
                    {
                        State = result.Error != null && result.Error.Code == NeteaseErrorCode.Cancelled
                            ? NeteaseQrState.Cancelled
                            : NeteaseQrState.Error,
                        Error = result.Error
                    };
                }

                switch (result.Value.Code)
                {
                    case 801:
                        return new NeteaseQrPollResult { State = NeteaseQrState.WaitingForScan };
                    case 802:
                        return new NeteaseQrPollResult { State = NeteaseQrState.WaitingForConfirm };
                    case 800:
                        return new NeteaseQrPollResult { State = NeteaseQrState.Expired };
                    case 803:
                        return await this.CompleteQrLoginAsync(session, cancellationToken);
                    default:
                        return new NeteaseQrPollResult
                        {
                            State = NeteaseQrState.Error,
                            Error = new NeteaseError(
                                NeteaseErrorCode.ApiChanged,
                                "Language_Netease_Service_Unavailable",
                                result.Value.Code)
                        };
                }
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        public async Task<NeteaseLoginResult> LoginWithCookieAsync(SecureString cookie, CancellationToken cancellationToken)
        {
            if (cookie == null || cookie.Length == 0)
            {
                return Failure(new NeteaseError(NeteaseErrorCode.InvalidCookie, "Language_Netease_Invalid_Cookie"));
            }

            await this.operationGate.WaitAsync(cancellationToken);

            try
            {
                Interlocked.Increment(ref this.loginGeneration);
                IReadOnlyDictionary<string, string> previousCookies = this.apiClient.SnapshotCookies();
                NeteaseSessionState previousState = this.state;
                NeteaseAccountProfile previousAccount = this.account;
                this.SetState(NeteaseSessionState.SigningIn, null);

                string cookieHeader = SecureStringToString(cookie);

                try
                {
                    NeteaseResult<IReadOnlyDictionary<string, string>> parsed = NeteaseCookieHeaderParser.Parse(cookieHeader);

                    if (!parsed.IsSuccess)
                    {
                        this.RestorePreviousSession(previousCookies, previousState, previousAccount);
                        return Failure(parsed.Error);
                    }

                    this.apiClient.ReplaceCookies(parsed.Value);
                    NeteaseResult<NeteaseAccountProfile> status = await this.apiClient.GetLoginStatusAsync(cancellationToken);

                    if (!status.IsSuccess)
                    {
                        this.RestorePreviousSession(previousCookies, previousState, previousAccount);
                        return Failure(status.Error);
                    }

                    NeteaseLoginResult persisted = await this.PersistAuthenticatedSessionAsync(status.Value, cancellationToken);

                    if (!persisted.IsSuccess)
                    {
                        this.RestorePreviousSession(previousCookies, previousState, previousAccount);
                    }

                    return persisted;
                }
                finally
                {
                    cookieHeader = null;
                }
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        public void CancelSignIn(long generation)
        {
            if (generation == 0 || generation != Interlocked.Read(ref this.loginGeneration))
            {
                return;
            }

            Interlocked.Increment(ref this.loginGeneration);

            if (this.state == NeteaseSessionState.SigningIn)
            {
                this.apiClient.ClearCookies();
                this.SetState(NeteaseSessionState.SignedOut, null);
            }
        }

        public async Task ExpireAsync(long expectedSessionGeneration)
        {
            await this.operationGate.WaitAsync();

            try
            {
                if (expectedSessionGeneration != Interlocked.Read(ref this.sessionGeneration) ||
                    this.state != NeteaseSessionState.SignedIn)
                {
                    return;
                }

                Interlocked.Increment(ref this.loginGeneration);
                Interlocked.Increment(ref this.sessionGeneration);
                this.apiClient.ClearCookies();
                await this.sessionStore.DeleteAsync();
                this.SetState(NeteaseSessionState.Expired, null);
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        public async Task LogoutAsync()
        {
            await this.operationGate.WaitAsync();

            try
            {
                Interlocked.Increment(ref this.loginGeneration);
                Interlocked.Increment(ref this.sessionGeneration);
                this.apiClient.ClearCookies();
                await this.sessionStore.DeleteAsync();
                this.SetState(NeteaseSessionState.SignedOut, null);
            }
            finally
            {
                this.operationGate.Release();
            }
        }

        private async Task<NeteaseQrPollResult> CompleteQrLoginAsync(NeteaseQrSession session, CancellationToken cancellationToken)
        {
            if (session.LoginGeneration != Interlocked.Read(ref this.loginGeneration))
            {
                return new NeteaseQrPollResult { State = NeteaseQrState.Cancelled };
            }

            NeteaseResult<NeteaseAccountProfile> status = await this.apiClient.GetLoginStatusAsync(cancellationToken);

            if (!status.IsSuccess)
            {
                this.SetState(IsAuthenticationError(status.Error)
                    ? NeteaseSessionState.Expired
                    : NeteaseSessionState.Error, null);

                return new NeteaseQrPollResult { State = NeteaseQrState.Error, Error = status.Error };
            }

            NeteaseLoginResult persisted = await this.PersistAuthenticatedSessionAsync(status.Value, cancellationToken);

            return new NeteaseQrPollResult
            {
                State = persisted.IsSuccess ? NeteaseQrState.Authorized : NeteaseQrState.Error,
                Account = persisted.Account,
                Error = persisted.Error
            };
        }

        private async Task<NeteaseLoginResult> PersistAuthenticatedSessionAsync(NeteaseAccountProfile authenticatedAccount, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string> cookies = this.apiClient.SnapshotCookies();

            if (cookies == null || cookies.Count == 0)
            {
                this.apiClient.ClearCookies();
                this.SetState(NeteaseSessionState.Error, null);
                return Failure(new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable"));
            }

            var snapshot = new NeteaseSessionSnapshot
            {
                Cookies = CopyCookies(cookies),
                Account = authenticatedAccount
            };

            NeteaseResult<bool> saveResult = await this.sessionStore.SaveAsync(snapshot, cancellationToken);

            if (!saveResult.IsSuccess)
            {
                this.apiClient.ClearCookies();
                this.SetState(NeteaseSessionState.Error, null);
                return Failure(saveResult.Error);
            }

            Interlocked.Increment(ref this.sessionGeneration);
            this.SetState(NeteaseSessionState.SignedIn, authenticatedAccount);

            return new NeteaseLoginResult
            {
                IsSuccess = true,
                Account = authenticatedAccount
            };
        }

        private void RestorePreviousSession(IReadOnlyDictionary<string, string> previousCookies, NeteaseSessionState previousState, NeteaseAccountProfile previousAccount)
        {
            this.apiClient.ReplaceCookies(previousCookies);
            this.SetState(previousState, previousAccount);
        }

        private void SetState(NeteaseSessionState newState, NeteaseAccountProfile newAccount)
        {
            this.state = newState;
            this.account = newAccount;
            this.SessionChanged(this, EventArgs.Empty);
        }

        private static bool IsAuthenticationError(NeteaseError error)
        {
            return error != null &&
                (error.Code == NeteaseErrorCode.AuthenticationRequired || error.Code == NeteaseErrorCode.SessionExpired);
        }

        private static NeteaseLoginResult Failure(NeteaseError error)
        {
            return new NeteaseLoginResult { IsSuccess = false, Error = error };
        }

        private static Dictionary<string, string> CopyCookies(IReadOnlyDictionary<string, string> cookies)
        {
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (cookies != null)
            {
                foreach (var cookie in cookies)
                {
                    copy[cookie.Key] = cookie.Value;
                }
            }

            return copy;
        }

        private static string SecureStringToString(SecureString value)
        {
            IntPtr pointer = IntPtr.Zero;

            try
            {
                pointer = Marshal.SecureStringToBSTR(value);
                return Marshal.PtrToStringBSTR(pointer);
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(pointer);
                }
            }
        }
    }
}
