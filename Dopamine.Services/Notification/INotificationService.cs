using Digimezzo.Foundation.WPF.Controls;
using System.Threading.Tasks;
using System.Windows;

namespace Dopamine.Services.Notification
{
    public interface INotificationService
    {
        bool SupportsSystemNotification { get; }
        bool SystemNotificationIsEnabled { get; set; }
        bool ShowNotificationWhenPlaying { get; set; }
        bool ShowNotificationWhenPausing { get; set; }
        bool ShowNotificationWhenResuming { get; set; }
        bool ShowNotificationControls { get; set; }

        Task ShowNotificationAsync();
        void HideNotification();

        /// <summary>
        /// Updates the window references used by notifications. Null arguments keep the existing reference.
        /// </summary>
        void SetApplicationWindows(Windows10BorderlessWindow mainWindow, Windows10BorderlessWindow playlistWindow, Window trayControlsWindow);
    }
}
