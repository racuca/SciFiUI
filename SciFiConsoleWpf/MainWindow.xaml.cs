using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
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


        // 지도
        private bool _hasTarget = false;
        private double _targetLat;
        private double _targetLng;


        public MainWindow()
        {
            InitializeComponent();

            InitMap();

            // 1) UI 애니메이션 시작
            var sb = (Storyboard)FindResource("UiAnimations");
            sb.Begin(this, true);

            // 2) 시계/진행률 타이머 설정
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }


        private void InitMap()
        {
            // GMap 기본 설정
            GMap.NET.GMaps.Instance.Mode = AccessMode.ServerAndCache;
            MapControl.MapProvider = GMapProviders.OpenStreetMap;

            // 시작 위치 (예: 서울 근처)
            MapControl.Position = new PointLatLng(37.5665, 126.9780);

            MapControl.Zoom = 12;
            MapControl.ShowCenter = false;

            // 지도 이벤트 연결
            // ⬇ 여기 수정
            MapControl.MouseLeftButtonDown += MapControl_MouseLeftButtonDown;
            MapControl.OnMapDrag += MapControl_OnMapDrag;
            MapControl.OnMapZoomChanged += MapControl_OnMapZoomChanged;
        }

        private void MapControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 클릭한 지점의 화면 좌표
            var p = e.GetPosition(MapControl);

            // 화면 좌표 → 위도/경도
            var latLng = MapControl.FromLocalToLatLng((int)p.X, (int)p.Y);

            _hasTarget = true;
            _targetLat = latLng.Lat;
            _targetLng = latLng.Lng;

            txtTargetGps.Text = $"LAT: {_targetLat:F6}   LON: {_targetLng:F6}";
            txtTargetSummary.Text = "AI SUMMARY: Object detected (dummy)";

            UpdateTargetOverlay();
        }

        private void MapControl_OnMapDrag()
        {
            UpdateTargetOverlay();
        }

        private void MapControl_OnMapZoomChanged()
        {
            UpdateTargetOverlay();
        }


        private void UpdateTargetOverlay()
        {
            if (!_hasTarget)
            {
                TargetDot.Visibility = Visibility.Collapsed;
                TargetLine.Visibility = Visibility.Collapsed;
                TargetDetailBox.Visibility = Visibility.Collapsed;
                return;
            }

            // 현재 지도에 보이는 영역 (위도/경도 범위)
            var view = MapControl.ViewArea;
            if (view.IsEmpty)
                return;

            // 타겟이 화면 영역 안에 있는지 체크
            bool inside =
                _targetLat <= view.Top &&
                _targetLat >= view.Bottom &&
                _targetLng >= view.Left &&
                _targetLng <= view.Right;

            if (!inside)
            {
                // 요구사항 3번: 화면에서 벗어나면 상세박스/선 숨김
                TargetDot.Visibility = Visibility.Collapsed;
                TargetLine.Visibility = Visibility.Collapsed;
                TargetDetailBox.Visibility = Visibility.Collapsed;
                return;
            }

            // (1) 타겟의 스크린 좌표 계산 (간단한 선형 변환)
            double mapWidth = MapControl.ActualWidth;
            double mapHeight = MapControl.ActualHeight;

            double xNorm = (_targetLng - view.Left) / (view.Right - view.Left);    // 0~1
            double yNorm = (view.Top - _targetLat) / (view.Top - view.Bottom);     // 0~1

            double x = xNorm * mapWidth;
            double y = yNorm * mapHeight;

            // 타겟 점 위치
            Canvas.SetLeft(TargetDot, x - TargetDot.Width / 2);
            Canvas.SetTop(TargetDot, y - TargetDot.Height / 2);
            TargetDot.Visibility = Visibility.Visible;

            // (2) 상세 박스를 어느 가장자리에 둘지 결정
            //    간단하게: 타겟이 왼쪽에 있으면 오른쪽에 박스, 오른쪽이면 왼쪽에 박스
            double boxWidth = TargetDetailBox.Width;
            double boxHeight = TargetDetailBox.Height;

            double margin = 20;
            double boxX;
            if (x < mapWidth / 2)
                boxX = mapWidth - boxWidth - margin;  // 오른쪽
            else
                boxX = margin;                         // 왼쪽

            double boxY = margin; // 일단 위쪽에 고정 (나중에 세로도 계산 가능)

            Canvas.SetLeft(TargetDetailBox, boxX);
            Canvas.SetTop(TargetDetailBox, boxY);
            TargetDetailBox.Visibility = Visibility.Visible;

            // (3) 타겟 → 상세박스 연결선
            TargetLine.X1 = x;
            TargetLine.Y1 = y;
            TargetLine.X2 = boxX + boxWidth / 2;
            TargetLine.Y2 = boxY + boxHeight / 2;
            TargetLine.Visibility = Visibility.Visible;
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
            //txtTargetInfo.Text = $"TGT: {TargetTransform.X:000.0} , {TargetTransform.Y:000.0}";

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
            var current = ((System.Windows.Media.SolidColorBrush)FovConeAnim.Stroke).Color;
            if (current == System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF))
            {
                FovConeAnim.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                FovConeAnim.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x99, 0x33));
            }
            else
            {
                FovConeAnim.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF));
                FovConeAnim.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x18, 0xE4, 0xFF));
            }
        }

        private void ShowLayer(Grid layerToShow)
        {
            LayerAnimation.Visibility = Visibility.Collapsed;
            LayerMap.Visibility = Visibility.Collapsed;
            LayerVideo.Visibility = Visibility.Collapsed;

            layerToShow.Visibility = Visibility.Visible;
        }


        private void btnShowAnim_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerAnimation);
        }

        private void btnShowMap_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerMap);
        }

        private void btnShowVideo_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerVideo);
        }
    }
}
