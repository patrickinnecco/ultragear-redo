using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ultragear.UI
{
    /// <summary>Label + slider + value. Raises Changed (debounced) when the user moves it; never when set from code.</summary>
    public sealed class SliderRow : Grid
    {
        readonly TextBlock _label, _value;
        public Slider Slider { get; }
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(90) };
        bool _fromCode;
        public string Unit { get; set; } = "";
        public event Action<SliderRow, int> Changed;
        public byte Code { get; }

        public SliderRow(string label, byte code, int min, int max, int step, int largeStep = 0)
        {
            Code = code;
            Margin = new Thickness(0, 6, 0, 8);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            _label = new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("Label"), Margin = new Thickness(2, 0, 0, 6) };
            Children.Add(_label);
            Slider = new Slider
            {
                Minimum = min, Maximum = max, SmallChange = step, LargeChange = largeStep > 0 ? largeStep : step * 5,
                TickFrequency = step, IsSnapToTickEnabled = true
            };
            AutomationProperties_SetName(Slider, label);
            SetRow(Slider, 1);
            Children.Add(Slider);
            _value = new TextBlock { Style = (Style)Application.Current.FindResource("ValueText") };
            SetRow(_value, 1); SetColumn(_value, 1);
            Children.Add(_value);

            Slider.ValueChanged += (s, e) =>
            {
                _value.Text = ((int)Slider.Value) + Unit;
                if (_fromCode) return;
                _debounce.Stop(); _debounce.Start();
            };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Changed?.Invoke(this, (int)Slider.Value); };
        }

        static void AutomationProperties_SetName(DependencyObject o, string n) => System.Windows.Automation.AutomationProperties.SetName(o, n);

        public bool IsDragging => Slider.IsMouseCaptureWithin || Mouse.LeftButton == MouseButtonState.Pressed && Slider.IsMouseOver;

        /// <summary>Shows a value read from the monitor. null = the monitor did not report it.</summary>
        public void SetFromMonitor(uint? v)
        {
            if (IsDragging) return;
            _fromCode = true;
            if (v is uint x && x >= Slider.Minimum && x <= Slider.Maximum)
            {
                Slider.Value = x; IsEnabled = true; _value.Text = x + Unit;
            }
            else
            {
                IsEnabled = false; _value.Text = "—";
            }
            _fromCode = false;
        }
    }
}
