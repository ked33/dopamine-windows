using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using System;

namespace Dopamine.Services.Shell
{
    public class AppVisibilityService : IAppVisibilityService
    {
        private bool isMainWindowInteractiveVisible;
        private bool isTrayControlsVisible;

        public bool IsMainWindowInteractiveVisible
        {
            get { return this.isMainWindowInteractiveVisible; }
        }

        public bool IsTrayControlsVisible
        {
            get { return this.isTrayControlsVisible; }
        }

        public bool IsBackgroundPlaybackMode
        {
            get { return !this.isMainWindowInteractiveVisible && !this.isTrayControlsVisible; }
        }

        public event EventHandler VisibilityChanged = delegate { };

        public void SetMainWindowInteractiveVisible(bool isVisible)
        {
            if (this.isMainWindowInteractiveVisible == isVisible)
            {
                return;
            }

            this.isMainWindowInteractiveVisible = isVisible;
            this.RaiseVisibilityChanged();
        }

        public void SetTrayControlsVisible(bool isVisible)
        {
            if (this.isTrayControlsVisible == isVisible)
            {
                return;
            }

            this.isTrayControlsVisible = isVisible;
            this.RaiseVisibilityChanged();
        }

        private void RaiseVisibilityChanged()
        {
            foreach (EventHandler handler in this.VisibilityChanged.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AppLog.Error("An app visibility change handler failed. Exception: {0}", ex.Message);
                }
            }
        }
    }
}
