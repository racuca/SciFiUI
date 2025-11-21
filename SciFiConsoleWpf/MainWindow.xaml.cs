using AForge.Video;
using AForge.Video.DirectShow;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using LibVLCSharp.Shared;
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


        bool mapInitialized = false;


        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;

        // 카메라 디바이스 목록
        private FilterInfoCollection _videoDevices;
        private bool videoInitialized = false;

        private VideoCaptureDevice _videoSource;


        public MainWindow()
        {
            InitializeComponent();

            // 30 fps로 애니메이션 프레임률 설정
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata(30)
            );

            // 1) UI 애니메이션 시작
            _uiStoryboard = (Storyboard)FindResource("UiAnimations");
            _uiStoryboard.Begin(this, true);

            // 2) 시계/진행률 타이머 설정
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += _timer_Tick;
            //_timer.Start();
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
                //MapControl.MouseLeftButtonDown -= MapControl_MouseLeftButtonDown;
                MapControl.MouseDown -= MapControl_MouseDown;
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

            // VLC 정리
            try
            {
                StopVideo();
                StopCamera();

                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }
                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                    _libVLC = null;
                }
            }
            catch { }

            // 마지막으로 애플리케이션 정리
            Application.Current.Shutdown();  // 이 줄은 선택이지만, 확실하게 끝낼 수 있음
        }


        private void InitMap()
        {
            // GMap 기본 설정
            GMap.NET.GMaps.Instance.Mode = AccessMode.ServerAndCache;
            MapControl.MapProvider = GMapProviders.BingSatelliteMap;

            // 시작 위치 (예: 서울 근처)
            MapControl.Position = new PointLatLng(37.5665, 126.9780);

            MapControl.Zoom = 12;
            MapControl.ShowCenter = false;

            // 지도 이벤트 연결
            // 여기 수정
            //MapControl.MouseLeftButtonDown += MapControl_MouseLeftButtonDown;
            MapControl.MouseDown += MapControl_MouseDown;
            
            // 지도 드래그를 오른쪽 버튼으로 하게 설정 (GMap 속성)
            MapControl.DragButton = MouseButton.Left;

            MapControl.OnMapDrag += MapControl_OnMapDrag;
            MapControl.OnMapZoomChanged += MapControl_OnMapZoomChanged;
        }

        private void MapControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 실제 어떤 버튼이 눌렸는지 여기서 정확히 알 수 있음
            //Console.WriteLine("Changed Button : " + e.ChangedButton);
            AddLogEntry($"MouseDown ChangedButton={e.ChangedButton}, Left={e.LeftButton}, Right={e.RightButton}");

            // 왼쪽 버튼일 때만 타겟/상세박스 생성
            if (e.ChangedButton == MouseButton.Right)
            {
                // 1) 클릭 지점 (MapControl 기준)
                var pMap = e.GetPosition(MapControl);

                // 2) 화면 좌표를 HudOverlay 기준으로 변환
                var pOverlay = MapControl.TranslatePoint(pMap, HudOverlay);

                // 3) 위도/경도
                var latLng = MapControl.FromLocalToLatLng((int)pMap.X, (int)pMap.Y);

                CreateDetailBox(latLng, pOverlay);
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                // 오른쪽 버튼은 지도 드래그 용으로 그냥 GMap에 넘김
                // (아무 것도 안 해도 DragButton = Right 덕분에 드래그 동작)
                // 필요하면 여기서 우클릭 메뉴 띄우거나, 전체 Clear 같은 동작도 가능
            }
        }

        private void InitVideo()
        {

            // LibVLC 초기화
            Core.Initialize();

            _libVLC = new LibVLC();
            _libVLC.Log += _libVLC_Log;
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            VideoViewControl.MediaPlayer = _mediaPlayer;

            // 카메라 목록 가져오기
            InitVideoSources();

        }

        private void _libVLC_Log(object sender, LogEventArgs e)
        {
            // 에러/경고 위주로만 로그 남기기
            if (e.Level == LogLevel.Error || e.Level == LogLevel.Warning)
            {
                Dispatcher.Invoke(() =>
                {
                    AddLogEntry($"VLC [{e.Level}] {e.Message}");
                });
            }
        }

        private void InitVideoSources()
        {
            cmbVideoSource.Items.Clear();

            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (_videoDevices.Count > 0)
                {
                    foreach (FilterInfo device in _videoDevices)
                    {
                        // 카메라 장치 추가
                        cmbVideoSource.Items.Add($"CAM: {device.Name}");
                    }
                }
            }
            catch
            {
                // 카메라 없는 경우 무시
            }

            // 마지막 항목: 네트워크 스트림
            cmbVideoSource.Items.Add("NETWORK STREAM (URL)");
            cmbVideoSource.SelectedIndex = 0;
        }

        private void MapControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Console.WriteLine("Mouse Left clicked");

            // 진짜 왼쪽 버튼일 때만 동작
            if (e.ChangedButton != MouseButton.Left)
                return;

            Console.WriteLine("Changed Button : " + e.ChangedButton.ToString());

            // 1) 클릭 지점 (MapControl 기준)
            var pMap = e.GetPosition(MapControl);

            // 2) 화면 좌표를 HudOverlay 기준으로 변환
            var pOverlay = MapControl.TranslatePoint(pMap, HudOverlay);

            // 3) 위도/경도
            var latLng = MapControl.FromLocalToLatLng((int)pMap.X, (int)pMap.Y);

            CreateDetailBox(latLng, pOverlay);

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
                StrokeDashArray = new DoubleCollection { 3, 2 }
                //X1 = clickPoint.X,
                //Y1 = clickPoint.Y,
                //X2 = clickPoint.X,
                //Y2 = clickPoint.Y
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

            // ⭐ 여기서 선의 좌표를 "타겟 LatLng 기준"으로 한 번 세팅
            SetInitialLine(detail, new Point(finalX, finalY));

            // 4) 애니메이션: 클릭 지점 → 가장자리 / 작게 → 최종 크기로
            AnimateDetailBox(detail, clickPoint, new Point(finalX, finalY));
        }


        private void SetInitialLine(TargetDetail detail, Point finalBoxPos)
        {
            // 1) 타겟 LatLng -> MapControl 로컬 픽셀
            var local = MapControl.FromLatLngToLocal(detail.LatLng);

            // 2) MapControl → HudOverlay 좌표로 변환
            var screen = MapControl.TranslatePoint(new Point(local.X, local.Y), HudOverlay);

            double x = screen.X;
            double y = screen.Y;

            // 3) 선 시작점 = 타겟 화면 좌표
            detail.Line.X1 = x;
            detail.Line.Y1 = y;

            // 4) 선 끝점 = 최종 박스 중심
            double boxCx = finalBoxPos.X + DetailBoxWidth / 2;
            double boxCy = finalBoxPos.Y + DetailBoxHeight / 2;

            detail.Line.X2 = boxCx;
            detail.Line.Y2 = boxCy;
        }

        private void AnimateDetailBox(TargetDetail detail, Point fromPoint, Point toPoint)
        {
            var box = detail.Box;
            double durationMs = 400;

            var sb = new Storyboard();

            // Canvas.Left
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

            // Canvas.Top
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

            // Width
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

            // Height
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

            foreach (var d in _detailBoxes)
            {
                // 1) 타겟 LatLng -> MapControl 로컬 픽셀 좌표
                var local = MapControl.FromLatLngToLocal(d.LatLng);   // GPoint

                // 2) MapControl 좌표계 -> HudOverlay 좌표계로 변환
                var screen = MapControl.TranslatePoint(
                    new Point(local.X, local.Y),
                    HudOverlay);

                double x = screen.X;
                double y = screen.Y;

                // 3) 선 시작점 = 타겟 화면 좌표
                d.Line.X1 = x;
                d.Line.Y1 = y;

                // 4) 선 끝점 = 박스 중심
                double boxX = Canvas.GetLeft(d.Box);
                double boxY = Canvas.GetTop(d.Box);
                double boxCx = boxX + d.Box.Width / 2;
                double boxCy = boxY + d.Box.Height / 2;

                d.Line.X2 = boxCx;
                d.Line.Y2 = boxCy;
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
            _uiStoryboard.Begin(this, true);  // 시작

            VideoSourcePanel.Visibility = Visibility.Collapsed;
        }

        private void btnShowMap_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerMap);
            _uiStoryboard.Stop(this);         // 중단

            VideoSourcePanel.Visibility = Visibility.Collapsed;

            if (!mapInitialized)
            {
                InitMap();
                mapInitialized = true;
            }
        }

        private void btnShowVideo_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerVideo);
            _uiStoryboard.Stop(this);         // 중단

            VideoSourcePanel.Visibility = Visibility.Visible;

            if (!videoInitialized)
            {
                InitVideo();
                videoInitialized = true;
            }
        }

        private void cmbVideoSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbVideoSource.SelectedItem == null) return;

            var text = cmbVideoSource.SelectedItem.ToString();

            if (text.StartsWith("CAM:"))
            {
                txtStreamUrl.Visibility = Visibility.Collapsed;
            }
            else
            {
                // NETWORK STREAM 선택시
                txtStreamUrl.Visibility = Visibility.Visible;
            }
        }

        private void StopVideo()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
            }

            // 스트림 모드에서는 VideoView만 보이게
            VideoViewControl.Visibility = Visibility.Visible;
        }

        private void btnConnectVideo_Click(object sender, RoutedEventArgs e)
        {
            if (cmbVideoSource.SelectedItem == null) return;

            var text = cmbVideoSource.SelectedItem.ToString();

            StopVideo();

            if (text.StartsWith("CAM:"))
            {
                string camName = text.Substring("CAM: ".Length);

                // 카메라 모드
                StartCamera(camName);
            }
            else
            {
                // 네트워크 스트림 모드
                StopCamera();  // 혹시 돌고 있던 카메라 정지
                CameraImage.Visibility = Visibility.Collapsed;
                VideoViewControl.Visibility = Visibility.Visible;

                string url = txtStreamUrl.Text?.Trim();
                if (!string.IsNullOrEmpty(url))
                {
                    StartNetworkStream(url);
                }
            }
        }

        private void btnStopVideo_Click(object sender, RoutedEventArgs e)
        {
            StopVideo();
            StopCamera();
        }

        private void TestSampleVideo()
        {
            // 샘플 MP4 HTTP
            string testUrl = txtStreamUrl.Text; // "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";

            var media = new Media(_libVLC, testUrl, FromType.FromLocation);
            media.AddOption(":network-caching=300");
            _mediaPlayer.Play(media);
        }

        private void StartCamera(string camName)
        {
            // VLC 재생 중이면 먼저 끄고
            StopVideo();

            // 이전 카메라도 정리
            StopCamera();


            if (_videoDevices == null || _videoDevices.Count == 0)
                return;

            // AForge에서 찾은 장치 중 이름이 camName과 같은 것 찾기
            FilterInfo device = null;
            foreach (FilterInfo d in _videoDevices)
            {
                if (d.Name == camName)
                {
                    device = d;
                    break;
                }
            }
            // 못 찾으면 첫 번째 장치로 fallback
            if (device == null)
                device = _videoDevices[0];

            _videoSource = new VideoCaptureDevice(device.MonikerString);

            // 해상도 선택 (원하면 지정)
            if (_videoSource.VideoCapabilities != null && _videoSource.VideoCapabilities.Length > 0)
            {
                // 적당한 해상도 하나 골라서 사용 (예: 가장 큰 해상도)
                var best = _videoSource.VideoCapabilities
                                       .OrderByDescending(v => v.FrameSize.Width * v.FrameSize.Height)
                                       .First();
                _videoSource.VideoResolution = best;
            }

            _videoSource.NewFrame += VideoSource_NewFrame;
            _videoSource.Start();

            // 카메라 모드에서는 CameraImage를 보이게, VideoView는 숨김
            CameraImage.Visibility = Visibility.Visible;
            VideoViewControl.Visibility = Visibility.Collapsed;
        }

        private void StopCamera()
        {
            if (_videoSource != null)
            {
                try
                {
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    if (_videoSource.IsRunning)
                        _videoSource.SignalToStop();
                    _videoSource.WaitForStop();
                }
                catch { }
                _videoSource = null;
            }

            CameraImage.Source = null;
            CameraImage.Visibility = Visibility.Collapsed;
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                // AForge 쪽 Bitmap은 다른 스레드에서 넘어오기 때문에 복사해야 함
                using (var frame = (System.Drawing.Bitmap)eventArgs.Frame.Clone())
                {
                    var bitmapImage = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream())
                    {
                        frame.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                        ms.Position = 0;

                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.StreamSource = ms;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                    }

                    // UI 스레드에서 Image.Source 갱신
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CameraImage.Source = bitmapImage;
                    }));
                }
            }
            catch
            {
                // 필요하면 로그 남기기
            }
        }

        private void StartNetworkStream(string url)
        {
            var media = new Media(_libVLC, url, FromType.FromLocation);

            // 실시간 스트림 튜닝 옵션 (원하면)
            media.AddOption(":network-caching=200");  // ms
            // media.AddOption(":rtsp-tcp");         // 필요하면 RTSP를 TCP로 강제

            _mediaPlayer.Play(media);
        }
    }
}
