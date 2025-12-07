using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SciFiUI.Controls
{
    /// <summary>
    /// PowerSystemPanel.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class PowerSystemPanel : UserControl
    {
        private enum PowerDisplayMode
        {
            Progress,
            Ring
        }

        private PowerDisplayMode _displayMode = PowerDisplayMode.Progress;

        private DispatcherTimer _timer;
        private readonly Random _rand = new Random();

        // Ring Path 핸들
        private Path _ringCore0Arc;
        private Path _ringCore1Arc;
        private Path _ringRamArc;
        private Path _ringGpuArc;

        public PowerSystemPanel()
        {
            InitializeComponent();

            Loaded += PowerSystemPanel_Loaded;
        }

        private void PowerSystemPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // 기본 모드: Progress
            SetDisplayMode(PowerDisplayMode.Progress);

            // 데모용 타이머 (실제 환경에서는 PerformanceCounter 로 교체 가능)
            if (_timer == null)
            {
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(800)
                };
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }

            // Ring 모드로 시작하고 싶으면 여기서 InitPowerRings 호출
            // (지금은 ContextMenu로 전환)
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // TODO: 여기서 실제 CPU/RAM 값을 가져오도록 변경 가능
            double core0 = 20 + _rand.NextDouble() * 60; // 20~80
            double core1 = 10 + _rand.NextDouble() * 70;
            double ram = 30 + _rand.NextDouble() * 50;
            double gpu = 15 + _rand.NextDouble() * 60;

            if (_displayMode == PowerDisplayMode.Progress)
            {
                Core0Bar.Value = core0;
                Core1Bar.Value = core1;
                RamBar.Value = ram;
                GpuBar.Value = gpu;
            }
            else
            {
                UpdatePowerRings(core0, core1, ram, gpu);
            }
        }

        private void SetDisplayMode(PowerDisplayMode mode)
        {
            _displayMode = mode;

            if (mode == PowerDisplayMode.Progress)
            {
                PowerSystemProgressPanel.Visibility = Visibility.Visible;
                PowerSystemRingPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                PowerSystemProgressPanel.Visibility = Visibility.Collapsed;
                PowerSystemRingPanel.Visibility = Visibility.Visible;

                if (_ringCore0Arc == null)
                {
                    InitPowerRings();
                }
            }
        }

        #region ContextMenu handlers

        private void PowerMode_Progress_Click(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(PowerDisplayMode.Progress);
            UpdateMenuChecks(sender, false);
        }

        private void PowerMode_Ring_Click(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(PowerDisplayMode.Ring);
            UpdateMenuChecks(sender, true);
        }

        private void UpdateMenuChecks(object sender, bool isRing)
        {
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm)
            {
                foreach (var item in cm.Items)
                {
                    if (item is MenuItem m)
                    {
                        if ((string)m.Header == "Progress Bars")
                            m.IsChecked = !isRing;
                        else if ((string)m.Header == "Ring Gauges")
                            m.IsChecked = isRing;
                    }
                }
            }
        }

        #endregion

        #region Ring meter

        private void InitPowerRings()
        {
            if (_ringCore0Arc != null) return;

            _ringCore0Arc = CreateRingMeter(RingCore0);
            _ringCore1Arc = CreateRingMeter(RingCore1);
            _ringRamArc = CreateRingMeter(RingRam);
            _ringGpuArc = CreateRingMeter(RingGpu);

            UpdatePowerRings(0, 0, 0, 0);
        }

        private Path CreateRingMeter(Canvas host)
        {
            double w = host.Width;
            double h = host.Height;
            double cx = w / 2.0;
            double cy = h / 2.0;
            double radius = Math.Min(w, h) * 0.42;
            double thickness = 4.0;

            var bg = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x26)),
                StrokeThickness = thickness,
                Opacity = 0.9
            };
            Canvas.SetLeft(bg, cx - radius);
            Canvas.SetTop(bg, cy - radius);
            host.Children.Add(bg);

            var arc = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x18, 0xE4, 0xFF)),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            host.Children.Add(arc);

            return arc;
        }

        private void UpdatePowerRings(double core0, double core1, double ram, double gpu)
        {
            if (_ringCore0Arc == null) return;

            SetRingValue(_ringCore0Arc, RingCore0, core0);
            SetRingValue(_ringCore1Arc, RingCore1, core1);
            SetRingValue(_ringRamArc, RingRam, ram);
            SetRingValue(_ringGpuArc, RingGpu, gpu);

            RingCore0Value.Text = $"{core0:0}%";
            RingCore1Value.Text = $"{core1:0}%";
            RingRamValue.Text = $"{ram:0}%";
            RingGpuValue.Text = $"{gpu:0}%";
        }

        private void SetRingValue(Path arc, Canvas host, double percent)
        {
            if (arc == null || host == null) return;

            percent = Math.Max(0, Math.Min(100, percent));
            double value = percent / 100.0;

            double w = host.Width;
            double h = host.Height;
            double cx = w / 2.0;
            double cy = h / 2.0;
            double radius = Math.Min(w, h) * 0.42;

            double startDeg = -210;
            double sweepDeg = 240 * value;

            double startRad = startDeg * Math.PI / 180.0;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

            Point startPoint = new Point(
                cx + radius * Math.Cos(startRad),
                cy + radius * Math.Sin(startRad));
            Point endPoint = new Point(
                cx + radius * Math.Cos(endRad),
                cy + radius * Math.Sin(endRad));

            bool isLargeArc = sweepDeg > 180;

            var fig = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false,
                IsFilled = false
            };

            var seg = new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            };

            fig.Segments.Clear();
            fig.Segments.Add(seg);

            var geom = new PathGeometry();
            geom.Figures.Add(fig);
            arc.Data = geom;
        }

        #endregion
    }
}
