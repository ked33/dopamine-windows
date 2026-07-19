namespace Dopamine.Services.Notification
{
    public interface IToastService
    {
        string Message { get; }

        bool IsVisible { get; }

        void Show(string message);
    }
}
