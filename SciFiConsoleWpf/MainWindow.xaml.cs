using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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



        private class TargetDetail
        {
            public Border Box;
            public Line Line;
            public GMapControl MiniMap;
            public PointLatLng LatLng;
            public bool OnLeftSide;
        }

        private readonly List<TargetDetail> _detailBoxes = new List<TargetDetail>();

        // 상수
        private const double DetailBoxWidth = 260;
        private const double DetailBoxHeight = 160;
        private const double DetailBoxMargin = 20;
        private const double DetailBoxGap = 10;

        private Storyboard _uiStoryboard;   // 애니메이션 저장용 필드

        public MainWindow()
        {
            InitializeComponent();

            InitMap();

            // 1) UI 애니메이션 시작
            _uiStoryboard = (Storyboard)FindResource("UiAnimations");
            _uiStoryboard.Begin(this, true);

            // 2) 시계/진행률 타이머 설정
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }


        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // 1) 타이머 정지
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= _timer_Tick;
                _timer = null;
            }

            // 2) 애니메이션 정지
            if (_uiStoryboard != null)
            {
                _uiStoryboard.Stop(this);
                _uiStoryboard = null;
            }

            // 3) GMap 정리
            if (MapControl != null)
            {
                // 이벤트 핸들러 해제
                MapControl.MouseLeftButtonDown -= MapControl_MouseLeftButtonDown;
                MapControl.OnMapDrag -= MapControl_OnMapDrag;
                MapControl.OnMapZoomChanged -= MapControl_OnMapZoomChanged;

                // 타일 캐싱/네트워크 작업 정리
                try
                {
                    MapControl.Manager.CancelTileCaching();
                    MapControl.Manager.Mode = AccessMode.CacheOnly;
                }
                catch { /* 무시해도 됨 */ }

                // WPF용 컨트롤에 Dispose() 있으면 호출해주고, 없으면 생략
                (MapControl as IDisposable)?.Dispose();
            }

            // 4) 혹시 남아 있는 다른 Thread / Task 있으면 여기서 정리

            // 마지막으로 애플리케이션 정리
            Application.Current.Shutdown();  // 이 줄은 선택이지만, 확실하게 끝낼 수 있음
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

            CreateDetailBox(latLng, p);
            UpdateDetailLines();

        }

        private void MapControl_OnMapDrag()
        {
            UpdateDetailBoxesVisibility();
            UpdateDetailLines();
        }

        private void MapControl_OnMapZoomChanged()
        {
            UpdateDetailBoxesVisibility();
            UpdateDetailLines();
        }


        private void CreateDetailBox(PointLatLng latLng, Point clickPoint)
        {
            // 1) 박스를 왼쪽/오른쪽 어느쪽에 둘지 결정
            double mapWidth = MapControl.ActualWidth;
            double mapHeight = MapControl.ActualHeight;

            bool placeOnRight = clickPoint.X < mapWidth / 2.0; // 클릭이 왼쪽이면 오른쪽에 박스
            double finalX = placeOnRight
                ? mapWidth - DetailBoxWidth - DetailBoxMargin
                : DetailBoxMargin;

            // 같은 쪽에 이미 있는 박스 개수만큼 아래로 쌓기
            int indexOnSide = 0;
            foreach (var d in _detailBoxes)
            {
                if (d.OnLeftSide == !placeOnRight)
                    indexOnSide++;
            }

            double finalY = DetailBoxMargin + indexOnSide * (DetailBoxHeight + DetailBoxGap);

            // 2) 선(Line) 만들기 (시작은 클릭 좌표 → 나중에 애니메이션으로 박스 중심까지 이동)
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x1F)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                X1 = clickPoint.X,
                Y1 = clickPoint.Y,
                X2 = clickPoint.X,
                Y2 = clickPoint.Y
            };
            HudOverlay.Children.Add(line);

            // 3) 상세박스(Border) + 내부 UI 구성
            var box = new Border
            {
                Width = 20,   // 처음엔 작게
                Height = 20,
                Background = (Brush)FindResource("BgMain"),
                BorderBrush = (Brush)FindResource("PanelBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 타이틀줄
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 텍스트
            grid.RowDefinitions.Add(new RowDefinition());                            // 미니맵

            // 타이틀 + 닫기 버튼
            var titlePanel = new DockPanel();
            var title = new TextBlock
            {
                Text = "TARGET DETAIL",
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            DockPanel.SetDock(title, Dock.Left);
            titlePanel.Children.Add(title);

            var closeBtn = new Button
            {
                Content = "X",
                Width = 18,
                Height = 18,
                Margin = new Thickness(4, 0, 0, 0),
                Style = (Style)FindResource("SciFiButton")  // 기존 SF 버튼 스타일 재사용
            };
            closeBtn.FontSize = 10;
            DockPanel.SetDock(closeBtn, Dock.Right);
            titlePanel.Children.Add(closeBtn);
            Grid.SetRow(titlePanel, 0);
            grid.Children.Add(titlePanel);

            // 텍스트 (GPS / 요약)
            var textPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };
            var gpsText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Text = $"LAT: {latLng.Lat:F6}   LON: {latLng.Lng:F6}"
            };
            var summaryText = new TextBlock
            {
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Text = "AI SUMMARY: (dummy)",
                TextWrapping = TextWrapping.Wrap
            };
            textPanel.Children.Add(gpsText);
            textPanel.Children.Add(summaryText);
            Grid.SetRow(textPanel, 1);
            grid.Children.Add(textPanel);

            // 미니맵 (2번 요구사항: 더 줌 인된 지도)
            var miniMap = new GMapControl
            {
                MapProvider = MapControl.MapProvider,
                MinZoom = 3,
                MaxZoom = 19,
                Position = latLng,
                Zoom = Math.Min(MapControl.Zoom + 2, 18), // 현재보다 더 IN
                ShowCenter = false,
                CanDragMap = false,
                MouseWheelZoomEnabled = false
            };
            Grid.SetRow(miniMap, 2);
            grid.Children.Add(miniMap);

            box.Child = grid;

            // Canvas에 초기 위치(클릭 지점)로 추가
            HudOverlay.Children.Add(box);
            Canvas.SetLeft(box, clickPoint.X);
            Canvas.SetTop(box, clickPoint.Y);

            // TargetDetail 구조체에 담아 관리
            var detail = new TargetDetail
            {
                Box = box,
                Line = line,
                MiniMap = miniMap,
                LatLng = latLng,
                OnLeftSide = !placeOnRight
            };
            _detailBoxes.Add(detail);

            // 닫기 버튼 동작
            closeBtn.Click += (s, e) => CloseDetailBox(detail);

            // 4) 애니메이션: 클릭 지점 → 가장자리 / 작게 → 최종 크기로
            AnimateDetailBox(detail, clickPoint, new Point(finalX, finalY));
        }


        private void AnimateDetailBox(TargetDetail detail, Point fromPoint, Point toPoint)
        {
            var box = detail.Box;
            var line = detail.Line;

            double durationMs = 400;

            var sb = new Storyboard();

            // 박스 이동 애니메이션 ------------------------
            var animLeft = new DoubleAnimation
            {
                From = fromPoint.X,
                To = toPoint.X,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animLeft, box);
            Storyboard.SetTargetProperty(animLeft, new PropertyPath("(Canvas.Left)"));
            sb.Children.Add(animLeft);

            var animTop = new DoubleAnimation
            {
                From = fromPoint.Y,
                To = toPoint.Y,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animTop, box);
            Storyboard.SetTargetProperty(animTop, new PropertyPath("(Canvas.Top)"));
            sb.Children.Add(animTop);

            // 박스 크기 애니메이션 ------------------------
            var animWidth = new DoubleAnimation
            {
                From = 20,
                To = DetailBoxWidth,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animWidth, box);
            Storyboard.SetTargetProperty(animWidth, new PropertyPath("Width"));
            sb.Children.Add(animWidth);

            var animHeight = new DoubleAnimation
            {
                From = 20,
                To = DetailBoxHeight,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animHeight, box);
            Storyboard.SetTargetProperty(animHeight, new PropertyPath("Height"));
            sb.Children.Add(animHeight);

            // 선(Line) 애니메이션 ------------------------
            var animLineX2 = new DoubleAnimation
            {
                From = fromPoint.X,
                To = toPoint.X + DetailBoxWidth / 2,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animLineX2, line);
            Storyboard.SetTargetProperty(animLineX2, new PropertyPath("X2"));
            sb.Children.Add(animLineX2);

            var animLineY2 = new DoubleAnimation
            {
                From = fromPoint.Y,
                To = toPoint.Y + DetailBoxHeight / 2,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animLineY2, line);
            Storyboard.SetTargetProperty(animLineY2, new PropertyPath("Y2"));
            sb.Children.Add(animLineY2);

            sb.Begin();
        }


        private void CloseDetailBox(TargetDetail detail)
        {
            HudOverlay.Children.Remove(detail.Box);
            HudOverlay.Children.Remove(detail.Line);
            _detailBoxes.Remove(detail);

            // 남아 있는 박스들 다시 정렬해도 되고 (선택 사항)
            //RearrangeDetailBoxes();
        }

        private void UpdateDetailBoxesVisibility()
        {
            var view = MapControl.ViewArea;
            if (view.IsEmpty) return;

            foreach (var d in _detailBoxes.ToArray())
            {
                bool inside =
                    d.LatLng.Lat <= view.Top &&
                    d.LatLng.Lat >= view.Bottom &&
                    d.LatLng.Lng >= view.Left &&
                    d.LatLng.Lng <= view.Right;

                if (!inside)
                {
                    // 화면 영역을 벗어난 타겟 → 상세박스를 닫는다
                    CloseDetailBox(d);
                }
            }
        }

        private void UpdateDetailLines()
        {
            if (_detailBoxes.Count == 0) return;
            if (MapControl.ActualWidth <= 0 || MapControl.ActualHeight <= 0) return;

            // 화면 중앙에 해당하는 픽셀 좌표
            var centerPixel = MapControl.FromLatLngToLocal(MapControl.Position);
            double w = MapControl.ActualWidth;
            double h = MapControl.ActualHeight;

            foreach (var d in _detailBoxes)
            {
                // 1) 타겟 위도/경도 → 맵 로컬 픽셀
                var targetPixel = MapControl.FromLatLngToLocal(d.LatLng);

                // 2) 화면 좌표로 변환
                double x = w / 2 + (targetPixel.X - centerPixel.X);
                double y = h / 2 + (targetPixel.Y - centerPixel.Y);

                // 선의 시작점 = 타겟 화면 좌표
                d.Line.X1 = x;
                d.Line.Y1 = y;

                // 선의 끝점 = 박스의 현재 중심 좌표
                double boxX = Canvas.GetLeft(d.Box);
                double boxY = Canvas.GetTop(d.Box);
                double boxCx = boxX + d.Box.Width / 2;
                double boxCy = boxY + d.Box.Height / 2;

                d.Line.X2 = boxCx;
                d.Line.Y2 = boxCy;
            }
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
