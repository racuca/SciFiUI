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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SciFiConsoleWpf
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {

        private DispatcherTimer _timer;
        private double _missionProgress = 0;
        private int _tickCount = 0;

        private Random _rand = new Random();

        public MainWindow()
        {
            InitializeComponent();

            // 1) UI 애니메이션 시작
            var sb = (Storyboard)FindResource("UiAnimations");
            sb.Begin(this, true);

            // 2) 시계/진행률 타이머 설정
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            _tickCount++;

            // 시계 업데이트
            txtClock.Text = DateTime.Now.ToString("HH:mm:ss");

            // 미션 진행률 데모 (0~100 반복)
            _missionProgress += 0.7;
            if (_missionProgress > 100) _missionProgress = 0;

            if (MissionProgress != null)
                MissionProgress.Value = _missionProgress;

            // 3) 가짜 텔레메트리 데이터 (ALT/SPD/HDG)
            double alt = 120 + 10 * Math.Sin(_tickCount / 10.0);
            double spd = 45 + 5 * Math.Cos(_tickCount / 12.0);
            double hdg = (_tickCount * 3) % 360;

            txtAlt.Text = $"ALT: {alt:000}m";
            txtSpd.Text = $"   SPD: {spd:000}kt";
            txtHdg.Text = $"   HDG: {hdg:000}°";

            // 4) 타겟 정보 텍스트 (타겟 Transform 사용)
            txtTargetInfo.Text = $"TGT: {TargetTransform.X:000.0} , {TargetTransform.Y:000.0}";

            // 5) 3초에 한 번씩 EVENT LOG 추가
            if (_tickCount % 6 == 0)   // 0.5초 * 6 = 3초
            {
                AddLogEntry("UAV#03  TELEMETRY UPDATE OK");
            }
        }


        private void AddLogEntry(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = $"{time}  {message}";
            EventLogList.Items.Insert(0, line);     // 최근 로그가 위로 오게
            if (EventLogList.Items.Count > 20)      // 20줄 넘으면 오래된 건 삭제
            {
                EventLogList.Items.RemoveAt(EventLogList.Items.Count - 1);
            }
        }

        private void btnArm_Click(object sender, RoutedEventArgs e)
        {
            AddLogEntry("UAV#03  ARM COMMAND SENT");
            LinkLed.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Lime);
        }

        private void btnDisarm_Click(object sender, RoutedEventArgs e)
        {
            AddLogEntry("UAV#03  DISARM COMMAND SENT");
            LinkLed.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x4D));
        }

        private void btnTrack_Click(object sender, RoutedEventArgs e)
        {
            AddLogEntry("GIMBAL  TARGET TRACKING TOGGLED");

            // FOV 색상 토글 느낌 (간단하게)
            var current = ((System.Windows.Media.SolidColorBrush)FovCone.Stroke).Color;
            if (current == System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF))
            {
                FovCone.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                FovCone.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x99, 0x33));
            }
            else
            {
                FovCone.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF));
                FovCone.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x18, 0xE4, 0xFF));
            }
        }
    }
}
