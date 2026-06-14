using Dopamine.Core.Settings;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Dopamine.Controls
{
    public class CrossFadeContentControl : ContentControl
    {
        private ContentPresenter PART_MainContent;
        private Shape PART_PaintArea;

        public double Duration
        {
            get { return Convert.ToDouble(GetValue(DurationProperty)); }
            set { SetValue(DurationProperty, value); }
        }

        public static readonly DependencyProperty DurationProperty = DependencyProperty.Register("Duration", typeof(double), typeof(CrossFadeContentControl), new PropertyMetadata(0.5));

        static CrossFadeContentControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CrossFadeContentControl), new FrameworkPropertyMetadata(typeof(CrossFadeContentControl)));
        }

        public override void OnApplyTemplate()
        {
            this.PART_MainContent = (ContentPresenter)GetTemplateChild("PART_MainContent");
            this.PART_PaintArea = (Shape)GetTemplateChild("PART_PaintArea");

            base.OnApplyTemplate();
        }
        
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            try
            {
                if (PART_PaintArea != null && PART_MainContent != null)
                {
                    PART_PaintArea.Fill = this.CreateBrushFromVisual(PART_MainContent);
                    this.BeginAnimateContentReplacement();
                }

                base.OnContentChanged(oldContent, newContent);
            }
            catch (Exception)
            {
            }
        }

        private Brush CreateBrushFromVisual(Visual iVisual)
        {

            if (iVisual == null)
            {
                throw new ArgumentNullException("iVisual");
            }

            dynamic target = new RenderTargetBitmap(Convert.ToInt32(this.ActualWidth), Convert.ToInt32(this.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            target.Render(iVisual);
            dynamic brush = new ImageBrush(target);
            brush.Freeze();
            return brush;
        }

        private void BeginAnimateContentReplacement()
        {
            if (!UiAnimationSettings.AreAnimationsEnabled)
            {
                PART_PaintArea.Opacity = 0.0;
                PART_MainContent.Opacity = 1.0;
                return;
            }

            PART_PaintArea.Opacity = 1.0;

            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(this.Duration)),
                AutoReverse = false
            };

            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(this.Duration)),
                AutoReverse = false
            };

            UiAnimationSettings.BeginAnimationOrSet(PART_PaintArea, OpacityProperty, fadeOutAnimation, 0.0);
            UiAnimationSettings.BeginAnimationOrSet(PART_MainContent, OpacityProperty, fadeInAnimation, 1.0);
        }
    }
}
