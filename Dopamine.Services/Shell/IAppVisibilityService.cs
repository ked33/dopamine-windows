using System;

namespace Dopamine.Services.Shell
{
    public interface IAppVisibilityService
    {
        bool IsMainWindowInteractiveVisible { get; }

        bool IsTrayControlsVisible { get; }

        bool IsBackgroundPlaybackMode { get; }

        event EventHandler VisibilityChanged;

        void SetMainWindowInteractiveVisible(bool isVisible);

        void SetTrayControlsVisible(bool isVisible);
    }
}
