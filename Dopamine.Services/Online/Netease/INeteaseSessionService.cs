using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseSessionService
    {
        NeteaseSessionState State { get; }

        NeteaseAccountProfile Account { get; }

        long SessionGeneration { get; }

        event EventHandler SessionChanged;

        Task RestoreAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<NeteaseQrSession>> BeginQrLoginAsync(CancellationToken cancellationToken);

        Task<NeteaseQrPollResult> PollQrLoginAsync(NeteaseQrSession session, CancellationToken cancellationToken);

        Task<NeteaseLoginResult> LoginWithCookieAsync(SecureString cookie, CancellationToken cancellationToken);

        Task ExpireAsync(long expectedSessionGeneration);

        void CancelSignIn(long loginGeneration);

        Task LogoutAsync();
    }
}
