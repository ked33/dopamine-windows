using Dopamine.Core.Settings;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dopamine.Controls
{
    public class SyncRing : Label
    {
        private bool isAnimationStateSubscribed;

        public double Middle
        {
            get { return Convert.ToDouble(GetValue(MiddleProperty)); }

            set { SetValue(MiddleProperty, value); }
        }

        public static readonly DependencyProperty MiddleProperty = DependencyProperty.Register("Middle", typeof(double), typeof(SyncRing), new PropertyMetadata(null));

        static SyncRing()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SyncRing), new FrameworkPropertyMetadata(typeof(SyncRing)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            this.SizeChanged -= SizeChangedHandler;
            this.SizeChanged += SizeChangedHandler;

            if (!this.isAnimationStateSubscribed)
            {
                UiAnimationSettings.Instance.PropertyChanged += this.UiAnimationSettingsPropertyChanged;
                this.IsEnabledChanged += this.IsEnabledChangedHandler;
                this.Unloaded += this.UnloadedHandler;
                this.isAnimationStateSubscribed = true;
            }

            this.UpdateAnimationState();
        }

        private void SizeChangedHandler(object sender, SizeChangedEventArgs e)
        {
            this.Middle = this.Width / 2;
        }

        private void UiAnimationSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UiAnimationSettings.AnimationsEnabled))
            {
                this.UpdateAnimationState();
            }
        }

        private void IsEnabledChangedHandler(object sender, DependencyPropertyChangedEventArgs e)
        {
            this.UpdateAnimationState();
        }

        private void UnloadedHandler(object sender, RoutedEventArgs e)
        {
            UiAnimationSettings.Instance.PropertyChanged -= this.UiAnimationSettingsPropertyChanged;
            this.IsEnabledChanged -= this.IsEnabledChangedHandler;
            this.Unloaded -= this.UnloadedHandler;
            this.isAnimationStateSubscribed = false;
            this.StopAnimation();
        }

        private void UpdateAnimationState()
        {
            if (!UiAnimationSettings.AreAnimationsEnabled || !this.IsEnabled)
            {
                this.StopAnimation();
                return;
            }

            RotateTransform rotateTransform = this.RenderTransform as RotateTransform;

            if (rotateTransform == null)
            {
                return;
            }

            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        private void StopAnimation()
        {
            RotateTransform rotateTransform = this.RenderTransform as RotateTransform;

            if (rotateTransform == null)
            {
                return;
            }

            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            rotateTransform.Angle = 0;
        }
    }
}
