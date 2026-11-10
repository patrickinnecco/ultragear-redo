using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ultragear.UI
{
    /// <summary>Small dark dialogs matching the app style.</summary>
    public static class Dialogs
    {
        static Window Make(Window owner, string title, out StackPanel body, out StackPanel buttons)
        {
            var w = new Window
            {
                Owner = owner, Title = title, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height, Width = 380, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner == null || !owner.IsVisible,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                FontFamily = new FontFamily("Segoe UI"), Topmost = owner == null || !owner.IsVisible
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(18), Background = (Brush)Application.Current.FindResource("PanelBg"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x55, 0x90)), BorderThickness = new Thickness(1), Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5 }
            };
            var root = new StackPanel();
            root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("SectionTitle"), Margin = new Thickness(0, 0, 0, 12) });
            body = new StackPanel();
            root.Children.Add(body);
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            root.Children.Add(buttons);
            border.Child = root;
            border.MouseLeftButtonDown += (s, e) => { try { w.DragMove(); } catch { } };
            w.Content = border;
            return w;
        }

        static Button Btn(string text, bool accent) => new Button
        {
            Content = text, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.FindResource(accent ? "AccentButton" : "PillButton")
        };

        static TextBlock Text(string t) => new TextBlock { Text = t, Style = (Style)Application.Current.FindResource("Label"), TextWrapping = TextWrapping.Wrap };

        public static bool Confirm(Window owner, string title, string message, string ok = "Confirmar", string cancel = "Cancelar")
        {
            var w = Make(owner, title, out var body, out var buttons);
            body.Children.Add(Text(message));
            var c = Btn(cancel, false); var o = Btn(ok, true);
            c.Click += (s, e) => w.DialogResult = false;
            o.Click += (s, e) => w.DialogResult = true;
            buttons.Children.Add(c); buttons.Children.Add(o);
            return w.ShowDialog() == true;
        }

        public static void Info(Window owner, string title, string message)
        {
            var w = Make(owner, title, out var body, out var buttons);
            body.Children.Add(new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBox
            {
                Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"), FontSize = 12.5
            }});
            var o = Btn("OK", true);
            o.Click += (s, e) => w.DialogResult = true;
            buttons.Children.Add(o);
            w.Width = 460;
            w.ShowDialog();
        }

        public static string Prompt(Window owner, string title, string message, string initial)
        {
            var w = Make(owner, title, out var body, out var buttons);
            body.Children.Add(Text(message));
            var tb = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0) };
            body.Children.Add(tb);
            var c = Btn("Cancelar", false); var o = Btn("Salvar", true);
            c.Click += (s, e) => w.DialogResult = false;
            o.Click += (s, e) => w.DialogResult = true;
            o.IsDefault = true;
            buttons.Children.Add(c); buttons.Children.Add(o);
            w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            return w.ShowDialog() == true ? tb.Text : null;
        }

        /// <summary>Returns true only when the user confirms before the countdown ends.</summary>
        public static Task<bool> ConfirmWithTimeout(Window owner, string title, string message, int seconds)
        {
            var w = Make(owner, title, out var body, out var buttons);
            var t = Text(message); body.Children.Add(t);
            var c = Btn("Reverter", false); var o = Btn($"Manter ({seconds})", true);
            int left = seconds;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => { left--; o.Content = $"Manter ({left})"; if (left <= 0) { timer.Stop(); w.DialogResult = false; } };
            c.Click += (s, e) => { timer.Stop(); w.DialogResult = false; };
            o.Click += (s, e) => { timer.Stop(); w.DialogResult = true; };
            buttons.Children.Add(c); buttons.Children.Add(o);
            w.Topmost = true;
            timer.Start();
            return Task.FromResult(w.ShowDialog() == true);
        }
    }
}
