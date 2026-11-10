using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Ultragear.Core;

namespace Ultragear.UI
{
    public partial class MainWindow : Window
    {
        readonly ScreenSettingsPage _screen = new();
        readonly SplitPage _split = new();
        readonly CalibrationPage _calib = new();
        readonly SettingsPage _settings = new();
        readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        bool _reallyClose;

        public MainWindow()
        {
            InitializeComponent();
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };
            Host.Notify += ShowToast;
            Host.Monitor.Busy += m => Dispatcher.BeginInvoke(new Action(() =>
            {
                BusyBox.Visibility = m == null ? Visibility.Collapsed : Visibility.Visible;
                BusyText.Text = m ?? "";
            }));
            Loaded += (s, e) => PlaceWindow();
            NavScreen.IsChecked = true;
        }

        void PlaceWindow()
        {
            var wa = SystemParameters.WorkArea;
            Height = Math.Min(900, wa.Height - 10);
            var s = Host.Settings;
            if (s.WindowLeft is double l && s.WindowTop is double t &&
                l >= SystemParameters.VirtualScreenLeft && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                t >= SystemParameters.VirtualScreenTop && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            { Left = l; Top = t; }
            else { Left = wa.Right - Width - 10; Top = wa.Bottom - Height; }
        }

        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == NavScreen) PageHost.Content = _screen;
            else if (sender == NavSplit) PageHost.Content = _split;
            else if (sender == NavCalib) PageHost.Content = _calib;
            else PageHost.Content = _settings;
        }

        public void ShowPage(string page)
        {
            ShowFromTray();
            if (page == "split") NavSplit.IsChecked = true; else NavScreen.IsChecked = true;
        }

        void ShowToast(string msg, bool error)
        {
            ToastText.Text = msg;
            ToastText.Foreground = error ? (System.Windows.Media.Brush)FindResource("Danger") : (System.Windows.Media.Brush)FindResource("TextPrimary");
            ToastBox.Visibility = Visibility.Visible;
            _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromSeconds(error ? 8 : 4); _toastTimer.Start();
            if (!IsVisible && error) Host.Tray?.Balloon(msg);
        }

        void Toast_Click(object s, MouseButtonEventArgs e) => ToastBox.Visibility = Visibility.Collapsed;

        public void RefreshFromState() => _screen.ApplyState(Host.Monitor.State);

        public async void OnDisplaysChanged() => await _screen.ReloadAsync();

        void Header_Drag(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); SavePosition(); }

        void SavePosition() { Host.Settings.WindowLeft = Left; Host.Settings.WindowTop = Top; Host.Settings.Save(); }

        void Minimize_Click(object s, RoutedEventArgs e) => Hide();

        void Close_Click(object s, RoutedEventArgs e)
        {
            if (Host.Settings.CloseToTray) { Hide(); Host.Tray.Balloon("O Ultragear continua ativo na bandeja do sistema."); }
            else ExitApp();
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate(); Topmost = true; Topmost = false;
        }

        public void ExitApp() { _reallyClose = true; SavePosition(); Close(); Application.Current.Shutdown(); }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_reallyClose) { e.Cancel = true; Hide(); }
            base.OnClosing(e);
        }
    }
}
