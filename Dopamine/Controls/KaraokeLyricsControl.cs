using CommonServiceLocator;
using Dopamine.Services.Playback;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dopamine.Controls
{
    public sealed class KaraokeLyricsControl : FrameworkElement
    {
        private const double LineHeight = 44.0;
        private static readonly Regex YrcLineRegex = new Regex(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
        private static readonly Regex YrcWordRegex = new Regex(@"\((\d+),(\d+),\d+\)([^\(]*)", RegexOptions.Compiled);
        private static readonly Regex LrcLineRegex = new Regex(@"^\[(\d{1,3}):(\d{1,2})(?:[\.:](\d{1,3}))?\](.*)$", RegexOptions.Compiled);
        private static readonly Regex MetadataTimeRegex = new Regex(@"[\""']t[\""']\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MetadataTextRegex = new Regex(@"[\""']tx[\""']\s*:\s*[\""']([^\""']*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IPlaybackService playbackService;
        private readonly DispatcherTimer animationTimer;
        private IReadOnlyList<LyricLine> lines = Array.Empty<LyricLine>();

        public static readonly DependencyProperty KaraokeLyricsProperty = DependencyProperty.Register(
            nameof(KaraokeLyrics), typeof(string), typeof(KaraokeLyricsControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnLyricsChanged));

        public static readonly DependencyProperty FallbackLyricsProperty = DependencyProperty.Register(
            nameof(FallbackLyrics), typeof(string), typeof(KaraokeLyricsControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnLyricsChanged));

        public static readonly DependencyProperty BaseBrushProperty = DependencyProperty.Register(
            nameof(BaseBrush), typeof(Brush), typeof(KaraokeLyricsControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, OnRenderingPropertyChanged));

        public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
            nameof(HighlightBrush), typeof(Brush), typeof(KaraokeLyricsControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnRenderingPropertyChanged));

        public KaraokeLyricsControl()
        {
            this.playbackService = ServiceLocator.Current.GetInstance<IPlaybackService>();
            this.animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            this.animationTimer.Tick += this.AnimationTimer_Tick;
            this.Loaded += this.KaraokeLyricsControl_Loaded;
            this.Unloaded += this.KaraokeLyricsControl_Unloaded;
            this.IsVisibleChanged += this.KaraokeLyricsControl_IsVisibleChanged;
        }

        public string KaraokeLyrics
        {
            get { return (string)this.GetValue(KaraokeLyricsProperty); }
            set { this.SetValue(KaraokeLyricsProperty, value); }
        }

        public string FallbackLyrics
        {
            get { return (string)this.GetValue(FallbackLyricsProperty); }
            set { this.SetValue(FallbackLyricsProperty, value); }
        }

        public Brush BaseBrush
        {
            get { return (Brush)this.GetValue(BaseBrushProperty); }
            set { this.SetValue(BaseBrushProperty, value); }
        }

        public Brush HighlightBrush
        {
            get { return (Brush)this.GetValue(HighlightBrushProperty); }
            set { this.SetValue(HighlightBrushProperty, value); }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (this.lines.Count == 0 || this.ActualWidth <= 0 || this.ActualHeight <= 0)
            {
                return;
            }

            double currentMilliseconds = this.playbackService.GetCurrentTime.TotalMilliseconds;
            int currentIndex = FindCurrentLine(this.lines, currentMilliseconds);
            int visibleRadius = Math.Max(2, (int)Math.Ceiling(this.ActualHeight / LineHeight / 2.0));
            int first = Math.Max(0, currentIndex - visibleRadius);
            int last = Math.Min(this.lines.Count - 1, currentIndex + visibleRadius);
            double centerY = this.ActualHeight / 2.0;

            for (int index = first; index <= last; index++)
            {
                LyricLine line = this.lines[index];
                double distance = Math.Abs(index - currentIndex);
                double opacity = Math.Max(0.28, 1.0 - distance * 0.18);
                FormattedText text = line.GetText(this, index == currentIndex, false);
                double x = Math.Max(0, (this.ActualWidth - text.WidthIncludingTrailingWhitespace) / 2.0);
                double y = centerY + (index - currentIndex) * LineHeight - text.Height / 2.0;
                drawingContext.PushOpacity(opacity);
                drawingContext.DrawText(text, new Point(x, y));
                drawingContext.Pop();

                if (index == currentIndex)
                {
                    double progress = GetLineProgress(line, currentMilliseconds);
                    double highlightWidth = text.WidthIncludingTrailingWhitespace * progress;

                    if (highlightWidth > 0)
                    {
                        drawingContext.PushClip(new RectangleGeometry(new Rect(x, y, highlightWidth, text.Height + 2)));
                        drawingContext.DrawText(line.GetText(this, true, true), new Point(x, y));
                        drawingContext.Pop();
                    }
                }
            }
        }

        private static void OnLyricsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var control = (KaraokeLyricsControl)dependencyObject;
            control.lines = ParseLyrics(control.KaraokeLyrics, control.FallbackLyrics);
            control.UpdateAnimationState();
            control.InvalidateVisual();
        }

        private static void OnRenderingPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var control = (KaraokeLyricsControl)dependencyObject;
            foreach (LyricLine line in control.lines) line.ClearTextCache();
            control.InvalidateVisual();
        }

        private static IReadOnlyList<LyricLine> ParseLyrics(string karaokeLyrics, string fallbackLyrics)
        {
            List<LyricLine> parsed = ParseYrc(karaokeLyrics);
            return parsed.Count > 0 ? parsed : ParseLrc(fallbackLyrics);
        }

        private static List<LyricLine> ParseYrc(string lyrics)
        {
            var result = new List<LyricLine>();

            foreach (string rawLine in SplitLines(lyrics))
            {
                Match match = YrcLineRegex.Match(rawLine.Trim());

                if (!match.Success)
                {
                    TryAddMetadataLine(result, rawLine);
                    continue;
                }

                double start = ParseDouble(match.Groups[1].Value);
                double duration = ParseDouble(match.Groups[2].Value);
                var words = new List<LyricWord>();
                string text = string.Empty;

                foreach (Match wordMatch in YrcWordRegex.Matches(match.Groups[3].Value))
                {
                    string wordText = wordMatch.Groups[3].Value;
                    text += wordText;
                    words.Add(new LyricWord(ParseDouble(wordMatch.Groups[1].Value), ParseDouble(wordMatch.Groups[2].Value), wordText));
                }

                text = NormalizeText(text);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(new LyricLine(start, Math.Max(duration, 1), text, words));
                }
            }

            return result.OrderBy(x => x.StartMilliseconds).ToList();
        }

        private static List<LyricLine> ParseLrc(string lyrics)
        {
            var result = new List<LyricLine>();

            foreach (string rawLine in SplitLines(lyrics))
            {
                Match match = LrcLineRegex.Match(rawLine.Trim());
                if (!match.Success)
                {
                    TryAddMetadataLine(result, rawLine);
                    continue;
                }

                double fraction = ParseDouble(match.Groups[3].Value);
                if (match.Groups[3].Value.Length == 2) fraction *= 10;
                else if (match.Groups[3].Value.Length == 1) fraction *= 100;
                double start = ParseDouble(match.Groups[1].Value) * 60000 + ParseDouble(match.Groups[2].Value) * 1000 + fraction;
                string text = NormalizeText(match.Groups[4].Value);
                if (!string.IsNullOrWhiteSpace(text)) result.Add(new LyricLine(start, 3000, text, Array.Empty<LyricWord>()));
            }

            result = result.OrderBy(x => x.StartMilliseconds).ToList();
            for (int i = 0; i < result.Count - 1; i++) result[i].DurationMilliseconds = Math.Max(1, result[i + 1].StartMilliseconds - result[i].StartMilliseconds);
            return result;
        }

        private static void TryAddMetadataLine(List<LyricLine> lines, string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || (!rawLine.Contains("\"tx\"") && !rawLine.Contains("'tx'"))) return;
            MatchCollection texts = MetadataTextRegex.Matches(rawLine);
            if (texts.Count == 0) return;
            string text = string.Concat(texts.Cast<Match>().Select(x => x.Groups[1].Value));
            Match time = MetadataTimeRegex.Match(rawLine);
            double start = time.Success ? ParseDouble(time.Groups[1].Value) : 0;
            text = NormalizeText(text);
            if (!string.IsNullOrWhiteSpace(text)) lines.Add(new LyricLine(start, 3000, text, Array.Empty<LyricWord>()));
        }

        private static string NormalizeText(string text)
        {
            return (text ?? string.Empty).Replace("\\\"", "\"").Trim();
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static double ParseDouble(string value)
        {
            double result;
            return double.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static int FindCurrentLine(IReadOnlyList<LyricLine> lyricLines, double milliseconds)
        {
            int low = 0;
            int high = lyricLines.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (lyricLines[middle].StartMilliseconds <= milliseconds) low = middle + 1;
                else high = middle - 1;
            }
            return Math.Max(0, Math.Min(lyricLines.Count - 1, high));
        }

        private static double GetLineProgress(LyricLine line, double milliseconds)
        {
            if (line.Words.Count == 0) return Clamp((milliseconds - line.StartMilliseconds) / line.DurationMilliseconds);
            double completedWidth = 0;
            double totalCharacters = Math.Max(1, line.Text.Length);
            foreach (LyricWord word in line.Words)
            {
                double wordProgress = Clamp((milliseconds - word.StartMilliseconds) / Math.Max(1, word.DurationMilliseconds));
                completedWidth += word.Text.Length * wordProgress;
            }
            return Clamp(completedWidth / totalCharacters);
        }

        private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));

        private FormattedText CreateText(string text, double fontSize, Brush brush)
        {
            Brush renderBrush = brush ?? Brushes.White;
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(this.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                fontSize, renderBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private FontFamily FontFamily => SystemFonts.MessageFontFamily;

        private void KaraokeLyricsControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.playbackService.PlaybackResumed += this.PlaybackService_StateChanged;
            this.playbackService.PlaybackPaused += this.PlaybackService_StateChanged;
            this.playbackService.PlaybackStopped += this.PlaybackService_StateChanged;
            this.playbackService.PlaybackSuccess += this.PlaybackService_PlaybackSuccess;
            this.UpdateAnimationState();
        }

        private void KaraokeLyricsControl_Unloaded(object sender, RoutedEventArgs e)
        {
            this.playbackService.PlaybackResumed -= this.PlaybackService_StateChanged;
            this.playbackService.PlaybackPaused -= this.PlaybackService_StateChanged;
            this.playbackService.PlaybackStopped -= this.PlaybackService_StateChanged;
            this.playbackService.PlaybackSuccess -= this.PlaybackService_PlaybackSuccess;
            this.animationTimer.Stop();
        }

        private void PlaybackService_StateChanged(object sender, EventArgs e) => this.UpdateAnimationState();
        private void PlaybackService_PlaybackSuccess(object sender, PlaybackSuccessEventArgs e) => this.UpdateAnimationState();
        private void KaraokeLyricsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => this.UpdateAnimationState();
        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (!this.ShouldAnimate()) this.animationTimer.Stop();
            else this.InvalidateVisual();
        }

        private void UpdateAnimationState()
        {
            if (this.ShouldAnimate()) this.animationTimer.Start();
            else this.animationTimer.Stop();
        }

        private bool ShouldAnimate()
        {
            return this.IsLoaded && this.IsVisible && this.lines.Count > 0 && this.playbackService.IsPlaying &&
                this.playbackService.QueueContext == PlaybackQueueContext.NeteasePersonalFm;
        }

        private sealed class LyricLine
        {
            public LyricLine(double start, double duration, string text, IReadOnlyList<LyricWord> words)
            {
                this.StartMilliseconds = start;
                this.DurationMilliseconds = duration;
                this.Text = text;
                this.Words = words;
            }
            public double StartMilliseconds { get; }
            public double DurationMilliseconds { get; set; }
            public string Text { get; }
            public IReadOnlyList<LyricWord> Words { get; }
            private FormattedText normalText;
            private FormattedText activeText;
            private FormattedText highlightText;

            public FormattedText GetText(KaraokeLyricsControl owner, bool isActive, bool isHighlight)
            {
                if (isHighlight)
                {
                    return this.highlightText ?? (this.highlightText = owner.CreateText(this.Text, 25.0, owner.HighlightBrush));
                }
                if (isActive)
                {
                    return this.activeText ?? (this.activeText = owner.CreateText(this.Text, 25.0, owner.BaseBrush));
                }
                return this.normalText ?? (this.normalText = owner.CreateText(this.Text, 18.0, owner.BaseBrush));
            }

            public void ClearTextCache()
            {
                this.normalText = null;
                this.activeText = null;
                this.highlightText = null;
            }
        }

        private sealed class LyricWord
        {
            public LyricWord(double start, double duration, string text)
            {
                this.StartMilliseconds = start;
                this.DurationMilliseconds = duration;
                this.Text = text;
            }
            public double StartMilliseconds { get; }
            public double DurationMilliseconds { get; }
            public string Text { get; }
        }
    }
}
