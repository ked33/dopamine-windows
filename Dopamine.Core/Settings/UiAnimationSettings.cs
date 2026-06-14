using Digimezzo.Foundation.Core.Settings;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dopamine.Core.Settings
{
    public class UiAnimationSettings : INotifyPropertyChanged
    {
        private const string SettingsNamespace = "Appearance";
        private const string SettingName = "EnableAnimations";

        private static readonly Lazy<UiAnimationSettings> LazyInstance = new Lazy<UiAnimationSettings>(() => new UiAnimationSettings());

        public static UiAnimationSettings Instance => LazyInstance.Value;

        public static bool AreAnimationsEnabled => Instance.AnimationsEnabled;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool AnimationsEnabled => GetAnimationsEnabled();

        public PopupAnimation FadePopupAnimation => this.AnimationsEnabled ? PopupAnimation.Fade : PopupAnimation.None;

        public PopupAnimation SlidePopupAnimation => this.AnimationsEnabled ? PopupAnimation.Slide : PopupAnimation.None;

        public double StandardDurationSeconds => this.AnimationsEnabled ? 0.5 : 0.0;

        public static void SetAnimationsEnabled(bool isEnabled)
        {
            SettingDefaults.SetSafe(SettingsNamespace, SettingName, isEnabled, true);
            Refresh();
        }

        public static void SyncWithPersistedSetting()
        {
            SyncWithSettingValue(GetAnimationsEnabled());
        }

        public static void SyncWithSettingValue(bool isEnabled)
        {
            if (!isEnabled)
            {
                SetAnimationsEnabled(false);
                return;
            }

            Refresh();
        }

        public static void Refresh()
        {
            GetAnimationsEnabled();
            Instance.NotifyAnimationPropertiesChanged();
        }

        private UiAnimationSettings()
        {
            SettingsClient.SettingChanged += (_, e) =>
            {
                if (SettingsClient.IsSettingChanged(e, SettingsNamespace, SettingName))
                {
                    this.NotifyAnimationPropertiesChanged();
                }
            };
        }

        public static void BeginAnimationOrSet(DependencyObject target, DependencyProperty property, AnimationTimeline animation, object disabledValue)
        {
            if (target == null)
            {
                return;
            }

            if (AreAnimationsEnabled && animation != null)
            {
                BeginAnimation(target, property, animation);
                return;
            }

            BeginAnimation(target, property, null);
            target.SetValue(property, disabledValue);
        }

        public static void BeginStoryboard(Storyboard storyboard)
        {
            if (storyboard == null)
            {
                return;
            }

            Storyboard storyboardToBegin = AreAnimationsEnabled ? storyboard : CreateInstantStoryboard(storyboard);
            BeginStoryboardSafely(storyboardToBegin, null);
        }

        public static void BeginStoryboard(Storyboard storyboard, FrameworkElement containingObject)
        {
            if (storyboard == null)
            {
                return;
            }

            Storyboard storyboardToBegin = AreAnimationsEnabled ? storyboard : CreateInstantStoryboard(storyboard);
            BeginStoryboardSafely(storyboardToBegin, containingObject);
        }

        private static void BeginStoryboardSafely(Storyboard storyboard, FrameworkElement containingObject)
        {
            try
            {
                if (containingObject == null)
                {
                    storyboard.Begin();
                }
                else
                {
                    storyboard.Begin(containingObject, true);
                }
            }
            catch (InvalidOperationException)
            {
                try
                {
                    storyboard.Begin();
                }
                catch (Exception)
                {
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool GetAnimationsEnabled()
        {
            return SettingDefaults.GetOrAdd(SettingsNamespace, SettingName, true, true);
        }

        private static void BeginAnimation(DependencyObject target, DependencyProperty property, AnimationTimeline animation)
        {
            UIElement uiElement = target as UIElement;

            if (uiElement != null)
            {
                uiElement.BeginAnimation(property, animation);
                return;
            }

            ContentElement contentElement = target as ContentElement;

            if (contentElement != null)
            {
                contentElement.BeginAnimation(property, animation);
                return;
            }

            Animatable animatable = target as Animatable;

            if (animatable != null)
            {
                animatable.BeginAnimation(property, animation);
            }
        }

        private static Storyboard CreateInstantStoryboard(Storyboard storyboard)
        {
            Storyboard instantStoryboard = storyboard.Clone();
            CollapseTimeline(instantStoryboard);
            return instantStoryboard;
        }

        private static void CollapseTimeline(Timeline timeline)
        {
            if (timeline == null)
            {
                return;
            }

            timeline.BeginTime = TimeSpan.Zero;
            timeline.Duration = new Duration(TimeSpan.Zero);
            timeline.RepeatBehavior = new RepeatBehavior(1);
            timeline.SpeedRatio = 1;

            TimelineGroup timelineGroup = timeline as TimelineGroup;

            if (timelineGroup != null)
            {
                foreach (Timeline child in timelineGroup.Children)
                {
                    CollapseTimeline(child);
                }
            }

            CollapseKeyFrames(timeline);
        }

        private static void CollapseKeyFrames(Timeline timeline)
        {
            DoubleAnimationUsingKeyFrames doubleAnimation = timeline as DoubleAnimationUsingKeyFrames;

            if (doubleAnimation != null)
            {
                foreach (DoubleKeyFrame frame in doubleAnimation.KeyFrames)
                {
                    frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero);
                }
            }

            ColorAnimationUsingKeyFrames colorAnimation = timeline as ColorAnimationUsingKeyFrames;

            if (colorAnimation != null)
            {
                foreach (ColorKeyFrame frame in colorAnimation.KeyFrames)
                {
                    frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero);
                }
            }

            ObjectAnimationUsingKeyFrames objectAnimation = timeline as ObjectAnimationUsingKeyFrames;

            if (objectAnimation != null)
            {
                foreach (ObjectKeyFrame frame in objectAnimation.KeyFrames)
                {
                    frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero);
                }
            }

            ThicknessAnimationUsingKeyFrames thicknessAnimation = timeline as ThicknessAnimationUsingKeyFrames;

            if (thicknessAnimation != null)
            {
                foreach (ThicknessKeyFrame frame in thicknessAnimation.KeyFrames)
                {
                    frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero);
                }
            }
        }

        private void NotifyAnimationPropertiesChanged()
        {
            this.OnPropertyChanged(nameof(this.AnimationsEnabled));
            this.OnPropertyChanged(nameof(this.FadePopupAnimation));
            this.OnPropertyChanged(nameof(this.SlidePopupAnimation));
            this.OnPropertyChanged(nameof(this.StandardDurationSeconds));
        }

        private void OnPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
