using System;
using System.Collections.Generic;

namespace Dopamine.Services.Online.GdMusic
{
    // Call under the API client's request lock; times come from its monotonic clock.
    public sealed class GdMusicRequestBudget
    {
        private const int WindowMilliseconds = 300000;
        private const int MaximumRequests = 50;
        private readonly Queue<long> requests = new Queue<long>();
        private long blockedUntil;

        public bool TryAcquire(long nowMilliseconds)
        {
            while (this.requests.Count > 0 && nowMilliseconds - this.requests.Peek() >= WindowMilliseconds)
            {
                this.requests.Dequeue();
            }

            if (nowMilliseconds < this.blockedUntil || this.requests.Count >= MaximumRequests)
            {
                return false;
            }

            this.requests.Enqueue(nowMilliseconds);
            return true;
        }

        public void BackOff(long nowMilliseconds, TimeSpan? retryAfter)
        {
            long delay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                ? (long)Math.Ceiling(retryAfter.Value.TotalMilliseconds)
                : WindowMilliseconds;
            this.blockedUntil = Math.Max(this.blockedUntil, nowMilliseconds + delay);
        }
    }
}
