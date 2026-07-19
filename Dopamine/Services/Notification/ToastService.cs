using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace Dopamine.Services.Notification
{
    public sealed class ToastService : BindableBase, IToastService
    {
        private const int MaximumQueuedMessages = 10;
        private readonly Queue<string> messages = new Queue<string>();
        private string message;
        private bool isVisible;
        private bool isProcessing;

        public string Message
        {
            get { return this.message; }
            private set { SetProperty<string>(ref this.message, value); }
        }

        public bool IsVisible
        {
            get { return this.isVisible; }
            private set { SetProperty<bool>(ref this.isVisible, value); }
        }

        public void Show(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || Application.Current?.Dispatcher == null)
            {
                return;
            }

            Action enqueue = () =>
            {
                while (this.messages.Count >= MaximumQueuedMessages)
                {
                    this.messages.Dequeue();
                }

                this.messages.Enqueue(message);
                this.ProcessQueueAsync();
            };

            if (Application.Current.Dispatcher.CheckAccess())
            {
                enqueue();
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(enqueue);
            }
        }

        private async void ProcessQueueAsync()
        {
            if (this.isProcessing)
            {
                return;
            }

            this.isProcessing = true;
            try
            {
                while (this.messages.Count > 0)
                {
                    this.Message = this.messages.Dequeue();
                    this.IsVisible = true;
                    await Task.Delay(TimeSpan.FromSeconds(4));
                    this.IsVisible = false;
                    await Task.Delay(TimeSpan.FromMilliseconds(150));
                }
            }
            finally
            {
                this.IsVisible = false;
                this.isProcessing = false;
            }
        }
    }
}
