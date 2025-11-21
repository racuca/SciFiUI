using AForge.Video;
using AForge.Video.DirectShow;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

        //private DispatcherTimer _timer;
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

        bool mapInitialized = false;


        private LibVLC _libVLC;
        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;

        // 카메라 디바이스 목록
        private FilterInfoCollection _videoDevices;
        private bool videoInitialized = false;

        private VideoCaptureDevice _videoSource;



        // 레이더 관련
        private DispatcherTimer _radarTimer;
        private double _radarAngle = 0;          // 현재 스윕 각도
        private bool _radarDetectedThisTurn = false;

        //private Storyboard _dataStreamStoryboard;

        private DispatcherTimer _dataStreamTimer;

        private class DataStreamBar
        {
            public ScaleTransform Scale;  // Y 스케일만 조정
            public double Target;         // 목표 높이 (0.2 ~ 1.0)
            public double Speed;          // 반응 속도 계수
        }

        private readonly List<DataStreamBar> _dataBars = new List<DataStreamBar>();





        // POWER SYSTEM 모니터링
        private PerformanceCounter[] _cpuCounters;   // Core0, Core1
        private PerformanceCounter _ramCounter;      // RAM %
        private DispatcherTimer _powerTimer;

        private ScaleTransform[] _cpuScales;         // Cpu0Scale, Cpu1Scale
        private ScaleTransform _ramScale;
        private ScaleTransform _gpuScale;

        // GPU 가상 값용
        private double _gpuDisplayValue = 0.0;

        // SIGNAL ANALYTICS
        private DispatcherTimer _signalTimer;
        private const int SignalHistoryCount = 40;
        private readonly List<double> _signalHistory = new List<double>();


        public MainWindow()
        {
            InitializeComponent();

            // 30 fps로 애니메이션 프레임률 설정
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata(30)
            );

            // 1) UI 애니메이션 시작
            //_dataStreamStoryboard = (Storyboard)FindResource("DataStreamAnimations");

            // 2) 시계/진행률 타이머 설정
            //_timer = new DispatcherTimer();
            //_timer.Interval = TimeSpan.FromSeconds(1);
            //_timer.Tick += _timer_Tick;
            //_timer.Start();

            RadarCanvas.Loaded += (s, e) => InitRadar();

            // Data Stream 초기화
            //DataStreamPanel.Loaded += (s, e) => InitDataStream();

            CPURAMPanel.Loaded += (s, e) => InitPowerSystemMonitor();

            SignalWaveHost.Loaded += (s, e) => InitSignalAnalytics();
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
            //if (_timer != null)
            //{
            //   _timer.Stop();
            //    _timer.Tick -= _timer_Tick;
            //    _timer = null;
            //}

            // 2) 애니메이션 정지
            //if (_dataStreamStoryboard != null)
            //{
            //    _dataStreamStoryboard.Stop(this);
            //    _dataStreamStoryboard = null;
            //}
            if (_dataStreamTimer != null)
            {
                _dataStreamTimer.Stop();
                _dataStreamTimer.Tick -= DataStreamTimer_Tick;
                _dataStreamTimer = null;
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

            //StopDataStream();

            if (_powerTimer != null)
            {
                _powerTimer.Stop();
                _powerTimer.Tick -= _powerTimer_Tick;
                _powerTimer = null;
            }

            if (_cpuCounters != null)
            {
                foreach (var c in _cpuCounters)
                    c?.Dispose();
                _cpuCounters = null;
            }

            _ramCounter?.Dispose();
            _ramCounter = null;


            if (_signalTimer != null)
            {
                _signalTimer.Stop();
                _signalTimer.Tick -= SignalTimer_Tick;
                _signalTimer = null;
            }

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


        private void InitRadar()
        {
            // 타겟 점 위치 (예: 반지름 90, 각도 45도 방향)
            // 필요하면 Map과 연동해서 바꿀 수 있음.
            double radius = 90;
            double deg = 45;           // 0 = 오른쪽, 90 = 위쪽 기준으로 쓰고 싶으면 보정
            double rad = deg * Math.PI / 180.0;

            double cx = RadarCanvas.ActualWidth > 0 ? RadarCanvas.ActualWidth / 2 : 130;
            double cy = RadarCanvas.ActualHeight > 0 ? RadarCanvas.ActualHeight / 2 : 130;

            double tx = cx + radius * Math.Cos(rad);
            double ty = cy - radius * Math.Sin(rad); // 화면 y축 반전

            // 타겟 점 배치
            Canvas.SetLeft(RadarTargetDot, tx - RadarTargetDot.Width / 2);
            Canvas.SetTop(RadarTargetDot, ty - RadarTargetDot.Height / 2);

            // 레이더 타이머 (50ms = 20fps 정도)
            _radarTimer = new DispatcherTimer();
            _radarTimer.Interval = TimeSpan.FromMilliseconds(50);
            _radarTimer.Tick += _radarTimer_Tick;
            _radarTimer.Start();
        }

        private void _radarTimer_Tick(object sender, EventArgs e)
        {
            // 각도 증가
            _radarAngle += 3; // 50ms * 3deg = 60deg/sec
            if (_radarAngle >= 360)
            {
                _radarAngle -= 360;
                _radarDetectedThisTurn = false;  // 한 바퀴 돌았으니 다시 탐지 가능
            }

            // 스윕 라인 회전
            RadarSweepRotate.Angle = _radarAngle;

            // 타겟 각도 계산
            DetectRadarTarget();
        }

        private void DetectRadarTarget()
        {
            double cx = RadarCanvas.ActualWidth / 2;
            double cy = RadarCanvas.ActualHeight / 2;

            if (cx <= 0 || cy <= 0)
                return;

            double tx = Canvas.GetLeft(RadarTargetDot) + RadarTargetDot.Width / 2;
            double ty = Canvas.GetTop(RadarTargetDot) + RadarTargetDot.Height / 2;

            double dx = tx - cx;
            double dy = cy - ty;  // y축 반전

            // atan2(dy, dx) : 0도 = 오른쪽, 반시계 방향 증가
            double targetDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (targetDeg < 0) targetDeg += 360;

            // 현재 스윕 각도와 비교
            double diff = Math.Abs(_radarAngle - targetDeg);
            if (diff > 180) diff = 360 - diff; // 0/360 기준 보정

            double tolerance = 3.0;  // 몇 도 안에 들어오면 찾은 걸로 볼지

            if (!_radarDetectedThisTurn && diff < tolerance)
            {
                _radarDetectedThisTurn = true;

                AddLogEntry($"RADAR TARGET DETECTED AT {targetDeg:0}°");
                
                //MessageBox.Show(
                //    $"타겟을 탐지했습니다!\n각도: {targetDeg:0}°",
                //    "RADAR",
                //    MessageBoxButton.OK,
                //    MessageBoxImage.Information);
            }
        }


        private void StartRadar()
        {
            if (_radarTimer != null && !_radarTimer.IsEnabled)
                _radarTimer.Start();
        }

        private void StopRadar()
        {
            if (_radarTimer != null && _radarTimer.IsEnabled)
                _radarTimer.Stop();
        }


        //private void StartDataStream()
        //{
        //    if (_dataStreamTimer == null)
        //        InitDataStream();
        //    else
        //        _dataStreamTimer.Start();
        //}

        //private void StopDataStream()
        //{
        //    _dataStreamTimer?.Stop();
        //}
        
        //private void InitDataStream()
        //{
        //    if (_dataStreamTimer != null)
        //        return; // 이미 초기화 되어 있으면 다시 안 함

        //    // 1) 막대들을 넣을 StackPanel 하나 만들기
        //    var host = DataStreamBarsHost;
        //    host.Children.Clear();

        //    var stack = new StackPanel
        //    {
        //        Orientation = Orientation.Horizontal,
        //        HorizontalAlignment = HorizontalAlignment.Center,
        //        VerticalAlignment = VerticalAlignment.Bottom,
        //        Margin = new Thickness(0, 4, 0, 0)
        //    };
        //    host.Children.Add(stack);

        //    // 2) 8개의 바 생성
        //    int barCount = 8;
        //    _dataBars.Clear();

        //    for (int i = 0; i < barCount; i++)
        //    {
        //        var border = new Border
        //        {
        //            Width = 12,
        //            Height = 80,
        //            Margin = new Thickness(4, 0, 4, 0),
        //            Background = (Brush)new SolidColorBrush(Color.FromRgb(0x08, 0x10, 0x17))
        //        };

        //        var rect = new Rectangle
        //        {
        //            VerticalAlignment = VerticalAlignment.Bottom,
        //            Fill = GetBarColor(i),
        //            Height = 80
        //        };

        //        Console.WriteLine("Bar Color : " + rect.Fill.ToString());

        //        // 아래를 기준으로 스케일 되도록 설정
        //        rect.RenderTransformOrigin = new Point(0.5, 1.0);
        //        var scale = new ScaleTransform
        //        {
        //            ScaleX = 1.0,
        //            ScaleY = 0.2  // 초기 높이
        //        };
        //        rect.RenderTransform = scale;

        //        border.Child = rect;
        //        stack.Children.Add(border);

        //        var bar = new DataStreamBar
        //        {
        //            Scale = scale,
        //            Target = 0.2 + _rand.NextDouble() * 0.8,         // 0.2~1.0
        //            Speed = 0.10 + _rand.NextDouble() * 0.15         // 0.10~0.25
        //        };
        //        _dataBars.Add(bar);
        //        Console.WriteLine($"Add bar {i} to UI");
        //    }

        //    Console.WriteLine("Host children: " + host.Children.Count);

        //    // 3) 타이머 설정 (약 16 fps)
        //    _dataStreamTimer = new DispatcherTimer
        //    {
        //        Interval = TimeSpan.FromMilliseconds(60)
        //    };
        //    _dataStreamTimer.Tick += DataStreamTimer_Tick;
        //    _dataStreamTimer.Start();
        //}

        private Brush GetBarColor(int index)
        {
            // 조금씩 다른 색으로
            //switch (index % 4)
            //{
            //    case 0: return new SolidColorBrush(Color.FromRgb(0x00, 0xE4, 0xFF)); // 사이언
            //    case 1: return new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)); // 라임
            //    case 2: return new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x33)); // 옐로우
            //    default: return new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)); // 레드
            //}

            // 전체 UI 컨셉에 맞춘 디지털 팔레트
            // index에 따라 적당히 로테이션
            byte a = 0xA0; // 반투명
            switch (index % 6)
            {
                case 0: // Primary Cyan
                    return new SolidColorBrush(Color.FromArgb(a, 0x18, 0xE4, 0xFF)); // #18E4FF

                case 1: // Soft Cyan
                    return new SolidColorBrush(Color.FromArgb(a, 0x4F, 0xD8, 0xFF)); // #4FD8FF

                case 2: // Neo Green
                    return new SolidColorBrush(Color.FromArgb(a, 0x3C, 0xFF, 0x9C)); // #3CFF9C

                case 3: // Muted Blue
                    return new SolidColorBrush(Color.FromArgb(a, 0x1F, 0x6F, 0xFF)); // #1F6FFF

                case 4: // Amber
                    return new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xC8, 0x57)); // #FFC857

                default: // Warning Red
                    return new SolidColorBrush(Color.FromArgb(a, 0xFF, 0x6B, 0x6B)); // #FF6B6B
            }
        }

        private void DataStreamTimer_Tick(object sender, EventArgs e)
        {
            foreach (var bar in _dataBars)
            {
                double current = bar.Scale.ScaleY;

                // 목표값을 향해 조금씩 보간 (lerp)
                double next = current + (bar.Target - current) * bar.Speed;

                // 최소/최대 한 번 더 제한
                if (next < 0.1) next = 0.1;
                if (next > 1.0) next = 1.0;

                bar.Scale.ScaleY = next;

                // 목표에 거의 도달하면 새 목표로 변경
                if (Math.Abs(bar.Target - next) < 0.03)
                {
                    bar.Target = 0.2 + _rand.NextDouble() * 0.8; // 0.2~1.0 사이 새로운 목표
                }
            }
        }



        private void InitPowerSystemMonitor()
        {
            // 1) XAML ScaleTransform 레퍼런스 연결
            _cpuScales = new[]
            {
                Cpu0Scale,
                Cpu1Scale
            };
            _ramScale = RamScale;
            _gpuScale = GpuScale;

            int coreCount = _cpuScales.Length;

            // 2) CPU 퍼포먼스 카운터 (Core0, Core1)
            _cpuCounters = new PerformanceCounter[coreCount];
            for (int i = 0; i < coreCount; i++)
            {
                _cpuCounters[i] = new PerformanceCounter(
                    "Processor",
                    "% Processor Time",
                    i.ToString());     // "0", "1"
                _cpuCounters[i].NextValue(); // 첫 값 버리기
            }

            // 3) RAM 퍼포먼스 카운터 (% 사용률)
            _ramCounter = new PerformanceCounter(
                "Memory",
                "% Committed Bytes In Use");
            _ramCounter.NextValue();

            // 4) 타이머 설정 (1초마다 갱신)
            _powerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _powerTimer.Tick += _powerTimer_Tick; ;
            _powerTimer.Start();
        }

        private void _powerTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // CPU Core 0,1
                for (int i = 0; i < _cpuScales.Length && i < _cpuCounters.Length; i++)
                {
                    float cpu = _cpuCounters[i].NextValue();   // 0~100
                    double norm = Math.Max(0.0, Math.Min(1.0, cpu / 100.0)); // 0~1

                    // 너무 완전 0이 되지 않게 0.05~1.0 범위
                    double scale = 0.05 + norm * 0.95;
                    _cpuScales[i].ScaleX = scale;
                }

                // RAM
                float ram = _ramCounter.NextValue();           // 0~100
                double ramNorm = Math.Max(0.0, Math.Min(1.0, ram / 100.0));
                _ramScale.ScaleX = 0.05 + ramNorm * 0.95;

                // GPU (임시: 전체 CPU 평균 기반 가상 값)
                double avgCpu =
                    (_cpuCounters[0].NextValue() +
                     _cpuCounters[1].NextValue()) / 2.0;

                double gpuTarget = Math.Max(0.0, Math.Min(1.0, avgCpu / 100.0));

                // 약간 느리게 따라오는 필터 걸어서 부드럽게
                _gpuDisplayValue += (gpuTarget - _gpuDisplayValue) * 0.3;
                _gpuScale.ScaleX = 0.05 + _gpuDisplayValue * 0.95;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PowerTimer error: " + ex.Message);
            }
        }


        private void InitSignalAnalytics()
        {
            if (_signalTimer != null)
                return; // 한 번만 초기화

            // 초기 히스토리 0으로 채워두기
            _signalHistory.Clear();
            for (int i = 0; i < SignalHistoryCount; i++)
            {
                _signalHistory.Add(0.0);
            }

            // 타이머: 200ms 정도면 부드럽게 움직임
            _signalTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _signalTimer.Tick += SignalTimer_Tick;
            _signalTimer.Start();
        }

        private void SignalTimer_Tick(object sender, EventArgs e)
        {
            // 1) 데모용 값 생성 (나중에 실제 값으로 교체)
            // 대략 60~100% 사이에서 왔다 갔다 하는 느낌
            double uplink = 60 + _rand.NextDouble() * 40;    // 60~100
            double downlink = 55 + _rand.NextDouble() * 45;  // 55~100

            double pktLoss = _rand.NextDouble() * 3.0;       // 0~3 %
            double latency = 30 + _rand.NextDouble() * 150;  // 30~180 ms

            // 2) 막대 (0~1 정규화 → ScaleX)
            UpdateSignalBar(SigUplinkScale, SigUplinkText, uplink, "UL");
            UpdateSignalBar(SigDownlinkScale, SigDownlinkText, downlink, "DL");

            // 3) PKT LOSS / LATENCY 텍스트 & 상태 색상
            PktLossText.Text = $"{pktLoss:0.0} %";
            LatencyText.Text = $"{latency:0} ms";

            // 색상: 초록/노랑/빨강 간단 룰
            PktLossIndicator.Fill = GetStatusColor(pktLoss, 1.0, 3.0);   // <1% 초록, <3% 노랑, 나머지 빨강
            LatencyIndicator.Fill = GetStatusColor(latency, 120, 250);   // <120ms 초록, <250ms 노랑, 나머지 빨강

            // 4) 웨이브폼: uplink 품질 기반으로 히스토리 추가
            double norm = Math.Max(0.0, Math.Min(1.0, uplink / 100.0));
            _signalHistory.Add(norm);
            if (_signalHistory.Count > SignalHistoryCount)
                _signalHistory.RemoveAt(0);

            UpdateSignalWavePolyline();
        }
        private void UpdateSignalBar(ScaleTransform scale,
                             TextBlock label,
                             double value,
                             string prefix)
        {
            double norm = Math.Max(0.0, Math.Min(1.0, value / 100.0));
            double sx = 0.05 + norm * 0.95;  // 최소 5% 길이 확보
            scale.ScaleX = sx;

            label.Text = $"{prefix}: {value:0}%";
        }

        private Brush GetStatusColor(double value, double warnThreshold, double dangerThreshold)
        {
            // value 기준으로 초록/노랑/빨강
            // - 좋은 쪽으로 작을수록 좋은 값 (loss, latency 같은 지표)
            if (value < warnThreshold)
                return new SolidColorBrush(Color.FromRgb(0x3C, 0xFF, 0x9C)); // 초록
            if (value < dangerThreshold)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57)); // 노랑
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));     // 빨강
        }


        private void UpdateSignalWavePolyline()
        {
            if (SignalWaveHost.ActualWidth <= 0 || SignalWaveHost.ActualHeight <= 0)
                return;

            double w = SignalWaveHost.ActualWidth;
            double h = SignalWaveHost.ActualHeight;

            int n = _signalHistory.Count;
            if (n < 2) return;

            double dx = w / (SignalHistoryCount - 1);
            var pts = new PointCollection();

            for (int i = 0; i < SignalHistoryCount; i++)
            {
                double v = (i < n) ? _signalHistory[i] : 0.0;   // 0~1
                double x = i * dx;
                double y = h - v * h;                           // 아래가 0, 위가 1

                pts.Add(new Point(x, y));
            }

            SignalWave.Points = pts;
        }






        /// <summary>
        /// Map 관련 이벤트: 마우스 왼쪽 버튼 클릭
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

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

            //// FOV 색상 토글 느낌 (간단하게)
            //var current = ((System.Windows.Media.SolidColorBrush)FovConeAnim.Stroke).Color;
            //if (current == System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF))
            //{
            //    FovConeAnim.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
            //    FovConeAnim.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x99, 0x33));
            //}
            //else
            //{
            //    FovConeAnim.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0xE4, 0xFF));
            //    FovConeAnim.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x18, 0xE4, 0xFF));
            //}
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
            
            StartRadar();

            // Data Stream도 같이 시작
            //StartDataStream();

            VideoSourcePanel.Visibility = Visibility.Collapsed;
        }

        private void btnShowMap_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerMap);

            StopRadar();
            //StopDataStream();

            if (!mapInitialized)
            {
                InitMap();
                mapInitialized = true;
            }

            VideoSourcePanel.Visibility = Visibility.Collapsed;
        }

        private void btnShowVideo_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerVideo);

            VideoSourcePanel.Visibility = Visibility.Visible;

            StopRadar();
            //StopDataStream();

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
