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

        private double _missionProgress = 0;
        private int _tickCount = 0;

        private Random _rand = new Random();


        private sealed class TargetDetail
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

        // Signal Analytics 영역 그래프용
        private const int SignalChartPoints = 30;
        private readonly List<double> _series1 = new List<double>(); // uplink 기반
        private readonly List<double> _series2 = new List<double>(); // downlink 기반
        private readonly List<double> _series3 = new List<double>(); // noise / interference



        #region Aircraft wireframe

        private sealed class Vec3
        {
            public double X, Y, Z;
            public Vec3(double x, double y, double z)
            {
                X = x; Y = y; Z = z;
            }
        }

        private readonly List<Vec3> _aircraftPoints = new List<Vec3>();

        // 면(페이스) 정의 + 폴리곤 UI
        private readonly List<int[]> _aircraftFaces = new List<int[]>();
        private readonly List<Polygon> _aircraftPolygons = new List<Polygon>();

        private DispatcherTimer _aircraftTimer;
        private double _aircraftAngle = 0;   // 회전 각도 (yaw 기준, 라디안)


        #endregion


        public MainWindow()
        {
            InitializeComponent();

            // 30 fps로 애니메이션 프레임률 설정
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata(30)
            );

            RadarCanvas.Loaded += (s, e) => InitRadar();

            CPURAMPanel.Loaded += (s, e) => InitPowerSystemMonitor();

            SignalWaveHost.Loaded += (s, e) => InitSignalAnalytics();

            AircraftCanvas.Loaded += (s, e) => InitAircraftModel();
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


            // 5) 3초에 한 번씩 EVENT LOG 추가
            if (_tickCount % 6 == 0)   // 0.5초 * 6 = 3초
            {
                AddLogEntry("UAV#03  TELEMETRY UPDATE OK");
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {

            // 3) GMap 정리
            if (MapControl != null)
            {
                // 이벤트 핸들러 해제
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
            catch (Exception ex)
            {
                Debug.WriteLine("VLC cleanup error: " + ex.Message);
            }

            
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

            if (_aircraftTimer != null)
            {
                _aircraftTimer.Stop();
                _aircraftTimer.Tick -= AircraftTimer_Tick;
                _aircraftTimer = null;
            }

            // 마지막으로 애플리케이션 정리
            Application.Current.Shutdown();  // 이 줄은 선택이지만, 확실하게 끝낼 수 있음
        }



        /////////////////////////////////////////////////////////////////////////////////////////////////
        /// 왼쪽 패널
        ////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Power System 모니터링 초기화
        /// </summary>
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
            _powerTimer.Tick += _powerTimer_Tick;
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


        /// <summary>
        /// 시그널 애널리틱스 초기화
        /// </summary>
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

            _series1.Clear();
            _series2.Clear();
            _series3.Clear();

            // 처음에는 0으로 채워두기
            for (int i = 0; i < SignalChartPoints; i++)
            {
                _series1.Add(0.3);
                _series2.Add(0.4);
                _series3.Add(0.2);
            }

            // 타이머: 200ms 정도면 부드럽게 움직임
            _signalTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _signalTimer.Tick += SignalTimer_Tick;
            _signalTimer.Start();
        }


        int signalAnalyticsCounter = 0;
        private void SignalTimer_Tick(object sender, EventArgs e)
        {
            signalAnalyticsCounter++;
            if (signalAnalyticsCounter % 5 == 0)
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

                // 3) 영역 그래프용 시리즈 업데이트 (0~1로 정규화)
                double s1 = Math.Max(0.0, Math.Min(1.0, uplink / 100.0));
                double s2 = Math.Max(0.0, Math.Min(1.0, downlink / 100.0));

                // s3는 잡음/간섭 느낌 – uplink/downlink 기준으로 약간 랜덤
                double noiseBase = 1.0 - Math.Max(s1, s2); // 품질 좋을수록 noise 작게
                double s3 = Math.Max(0.0, Math.Min(1.0, noiseBase + (_rand.NextDouble() - 0.5) * 0.2));

                AppendSeries(_series1, s1);
                AppendSeries(_series2, s2);
                AppendSeries(_series3, s3);
            }

            // 4) 그래프 다시 그리기
            UpdateSignalAreaChart();

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


        private void AppendSeries(List<double> series, double value)
        {
            series.Add(value);
            if (series.Count > SignalChartPoints)
                series.RemoveAt(0);
        }

        private void UpdateSignalAreaChart()
        {
            double w = SignalChartHost.ActualWidth;
            double h = SignalChartHost.ActualHeight;
            if (w <= 0 || h <= 0) return;

            int n = SignalChartPoints;
            double dx = w / (n - 1);

            // 1) 시리즈 → Polygon Points 로 변환
            PointCollection BuildArea(List<double> s)
            {
                var pts = new PointCollection();

                // 윗 라인 (좌 → 우)
                for (int i = 0; i < n; i++)
                {
                    double v = (i < s.Count) ? s[i] : 0.0;
                    double x = i * dx;
                    double y = h - v * h;   // 아래쪽이 0, 위쪽이 1
                    pts.Add(new Point(x, y));
                }

                // 밑변 (우 → 좌)
                pts.Add(new Point((n - 1) * dx, h));
                pts.Add(new Point(0, h));

                return pts;
            }

            SigArea1.Points = BuildArea(_series1);
            SigArea2.Points = BuildArea(_series2);
            SigArea3.Points = BuildArea(_series3);
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


        /////////////////////////////////////////////////////////////////////////////////////////////////
        /// 중간 패널 
        ///////////////////////////////////////////////////////////////////////////////////////////////////

        private void InitAircraftModel()
        {
            if (_aircraftTimer != null)
                return;

            // 1) 3D 포인트 정의 (뾰족한 노즈 + 두께 있는 동체 + 주날개)
            _aircraftPoints.Clear();

            // 좌표계: X = 전방(+), Y = 좌/우(+ 오른쪽), Z = 위(+)

            // --- 코(tip) ---
            _aircraftPoints.Add(new Vec3(1.2, 0.0, 0.0));   // 0 tip (가장 뾰족한 앞부분)

            // --- 동체 앞 단면 (노즈 단면 사각형) ---
            _aircraftPoints.Add(new Vec3(1.0, 0.15, 0.15)); // 1 nose TL
            _aircraftPoints.Add(new Vec3(1.0, -0.15, 0.15)); // 2 nose TR
            _aircraftPoints.Add(new Vec3(1.0, -0.15, -0.15)); // 3 nose BR
            _aircraftPoints.Add(new Vec3(1.0, 0.15, -0.15)); // 4 nose BL

            // --- 동체 뒤 단면 (꼬리쪽 사각형, 살짝 좁아지고 높아짐) ---
            _aircraftPoints.Add(new Vec3(-1.0, 0.10, 0.22)); // 5 tail TL
            _aircraftPoints.Add(new Vec3(-1.0, -0.10, 0.22)); // 6 tail TR
            _aircraftPoints.Add(new Vec3(-1.0, -0.10, -0.12)); // 7 tail BR
            _aircraftPoints.Add(new Vec3(-1.0, 0.10, -0.12)); // 8 tail BL

            // --- 메인 날개 (동체 옆에 붙은 사다리꼴) ---
            // 오른쪽 주날개 (+Y)
            _aircraftPoints.Add(new Vec3(0.2, 0.20, 0.02)); //  9 right wing root front
            _aircraftPoints.Add(new Vec3(-0.2, 0.20, 0.02)); // 10 right wing root rear
            _aircraftPoints.Add(new Vec3(0.2, 1.40, 0.02)); // 11 right wing tip front
            _aircraftPoints.Add(new Vec3(-0.2, 1.40, 0.02)); // 12 right wing tip rear

            // 왼쪽 주날개 (-Y)
            _aircraftPoints.Add(new Vec3(0.2, -0.20, 0.02)); // 13 left wing root front
            _aircraftPoints.Add(new Vec3(-0.2, -0.20, 0.02)); // 14 left wing root rear
            _aircraftPoints.Add(new Vec3(0.2, -1.40, 0.02)); // 15 left wing tip front
            _aircraftPoints.Add(new Vec3(-0.2, -1.40, 0.02)); // 16 left wing tip rear

            // --- 수평 꼬리날개 ---
            // 오른쪽 꼬리날개
            _aircraftPoints.Add(new Vec3(-0.8, 0.18, 0.02)); // 17 right tail wing root front
            _aircraftPoints.Add(new Vec3(-1.1, 0.18, 0.02)); // 18 right tail wing root rear
            _aircraftPoints.Add(new Vec3(-1.1, 0.55, 0.02)); // 19 right tail wing tip

            // 왼쪽 꼬리날개
            _aircraftPoints.Add(new Vec3(-0.8, -0.18, 0.02)); // 20 left tail wing root front
            _aircraftPoints.Add(new Vec3(-1.1, -0.18, 0.02)); // 21 left tail wing root rear
            _aircraftPoints.Add(new Vec3(-1.1, -0.55, 0.02)); // 22 left tail wing tip

            // --- 수직 꼬리날개 ---
            _aircraftPoints.Add(new Vec3(-1.1, 0.0, 0.00));  // 23 fin base rear
            _aircraftPoints.Add(new Vec3(-1.1, 0.0, 0.28));  // 24 fin mid
            _aircraftPoints.Add(new Vec3(-1.1, 0.0, 0.60));  // 25 fin top

            // 2) 면(face) 정의
            _aircraftFaces.Clear();

            // === 뾰족한 코(노즈) 부분: tip + 앞 단면 사각형 4개의 삼각형 ===
            _aircraftFaces.Add(new[] { 0, 1, 2 }); // 위쪽
            _aircraftFaces.Add(new[] { 0, 2, 3 }); // 오른쪽
            _aircraftFaces.Add(new[] { 0, 3, 4 }); // 아래
            _aircraftFaces.Add(new[] { 0, 4, 1 }); // 왼쪽

            // === 동체 박스 6면 (앞 단면 1~4 ↔ 뒤 단면 5~8) ===
            // 윗면
            _aircraftFaces.Add(new[] { 1, 2, 6, 5 });
            // 아랫면
            _aircraftFaces.Add(new[] { 4, 3, 7, 8 });
            // 왼쪽면 (+Y)
            _aircraftFaces.Add(new[] { 1, 4, 8, 5 });
            // 오른쪽면 (-Y)
            _aircraftFaces.Add(new[] { 2, 3, 7, 6 });
            // 앞면 (노즈 단면)
            _aircraftFaces.Add(new[] { 1, 2, 3, 4 });
            // 뒷면 (꼬리 단면)
            _aircraftFaces.Add(new[] { 5, 6, 7, 8 });

            // === 메인 날개 (사다리꼴 2개) ===
            // 오른쪽 주날개
            _aircraftFaces.Add(new[] { 9, 11, 12, 10 });
            // 왼쪽 주날개
            _aircraftFaces.Add(new[] { 13, 15, 16, 14 });

            // === 수평 꼬리날개 (삼각형 2개) ===
            _aircraftFaces.Add(new[] { 17, 19, 18 }); // 오른쪽 꼬리날개
            _aircraftFaces.Add(new[] { 20, 22, 21 }); // 왼쪽 꼬리날개

            // === 수직 꼬리날개 (삼각형) ===
            _aircraftFaces.Add(new[] { 23, 24, 25 });

            // 3) Canvas에 폴리곤 생성
            AircraftCanvas.Children.Clear();

            var title = new TextBlock
            {
                Text = "AIRFRAME ORIENTATION",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Canvas.SetLeft(title, 0);
            Canvas.SetTop(title, 0);
            AircraftCanvas.Children.Add(title);

            _aircraftPolygons.Clear();

            foreach (var face in _aircraftFaces)
            {
                var poly = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x44, 0x18, 0xE4, 0xFF)),   // 반투명 시안
                    Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0x18, 0xE4, 0xFF)), // 외곽선
                    StrokeThickness = 0.8
                };
                _aircraftPolygons.Add(poly);
                AircraftCanvas.Children.Add(poly);
            }

            // 4) 회전 타이머 시작 (기존과 동일)
            _aircraftTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _aircraftTimer.Tick += AircraftTimer_Tick;
            _aircraftTimer.Start();
        }

        private void AircraftTimer_Tick(object sender, EventArgs e)
        {
            _aircraftAngle += 0.03;   // 회전 속도 (작을수록 천천히)
            if (_aircraftAngle > Math.PI * 2)
                _aircraftAngle -= Math.PI * 2;

            UpdateAircraftProjection();
        }

        private void UpdateAircraftProjection()
        {
            if (AircraftCanvas.ActualWidth <= 0 || AircraftCanvas.ActualHeight <= 0)
                return;

            double cx = AircraftCanvas.ActualWidth / 2.0;
            double cy = AircraftCanvas.ActualHeight / 2.0 + 10;
            double scale = Math.Min(AircraftCanvas.ActualWidth, AircraftCanvas.ActualHeight) * 0.35;

            double yaw = _aircraftAngle;
            double pitch = 0.4;
            double roll = Math.Sin(_aircraftAngle * 0.7) * 0.2;

            double cyaw = Math.Cos(yaw);
            double syaw = Math.Sin(yaw);
            double cp = Math.Cos(pitch);
            double sp = Math.Sin(pitch);
            double cr = Math.Cos(roll);
            double sr = Math.Sin(roll);

            var projected = new List<Point>(_aircraftPoints.Count);

            for (int i = 0; i < _aircraftPoints.Count; i++)
            {
                var p = _aircraftPoints[i];

                // Yaw → Pitch → Roll
                double x1 = p.X * cyaw - p.Y * syaw;
                double y1 = p.X * syaw + p.Y * cyaw;
                double z1 = p.Z;

                double x2 = x1;
                double y2 = y1 * cp - z1 * sp;
                double z2 = y1 * sp + z1 * cp;

                double x3 = x2 * cr + z2 * sr;
                double y3 = y2;
                double z3 = -x2 * sr + z2 * cr;

                double depth = 2.0 + y3;
                double sx = x3 / depth;
                double sy = z3 / depth;

                double px = cx + sx * scale;
                double py = cy - sy * scale;

                projected.Add(new Point(px, py));
            }

            // 면(Polygon) 갱신
            for (int fi = 0; fi < _aircraftFaces.Count; fi++)
            {
                var idx = _aircraftFaces[fi];
                var poly = _aircraftPolygons[fi];

                var pts = new PointCollection();
                for (int k = 0; k < idx.Length; k++)
                {
                    pts.Add(projected[idx[k]]);
                }
                poly.Points = pts;
            }
        }

        /// <summary>
        /// 맵 초기화
        /// </summary>
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
            MapControl.MouseDown += MapControl_MouseDown;
            
            // 지도 드래그를 오른쪽 버튼으로 하게 설정 (GMap 속성)
            MapControl.DragButton = MouseButton.Left;

            MapControl.OnMapDrag += MapControl_OnMapDrag;
            MapControl.OnMapZoomChanged += MapControl_OnMapZoomChanged;
        }

        private void MapControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 실제 어떤 버튼이 눌렸는지 여기서 정확히 알 수 있음
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


        /// <summary>
        /// Video 초기화
        /// </summary>
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

        /// <summary>
        /// 레이더 초기화
        /// </summary>
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

            RadarTargetDot.Visibility = Visibility.Hidden;

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
                //_radarDetectedThisTurn = false;  // 한 바퀴 돌았으니 다시 탐지 가능
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

            double tolerance = 10.0;  // 몇 도 안에 들어오면 찾은 걸로 볼지

            if (diff < tolerance)
            {
                
                RadarTargetDot.Visibility = Visibility.Visible; 

                AddLogEntry($"RADAR TARGET DETECTED AT {targetDeg:0}°");
            }
            else
            {
                RadarTargetDot.Visibility = Visibility.Hidden;
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

        private void StartAircraft()
        {
            if (_aircraftTimer != null && !_aircraftTimer.IsEnabled)
                _aircraftTimer.Start();
        }

        private void StopAircraft()
        {
            if (_aircraftTimer != null && _aircraftTimer.IsEnabled)
                _aircraftTimer.Stop();
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
            if (_detailBoxes.Count > 5)
                return;

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


        ///////////////////////////////////////////////////////////////////////////////////////////
        /// 오른쪽 패널
        ////////////////////////////////////////////////////////////////////////////////////////// 
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
            StartAircraft();

            VideoSourcePanel.Visibility = Visibility.Collapsed;
        }

        private void btnShowMap_Click(object sender, RoutedEventArgs e)
        {
            ShowLayer(LayerMap);

            StopRadar();
            StopAircraft();

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
            StopAircraft();
            
            if (!videoInitialized)
            {
                Task.Run(() =>
                {
                    InitVideo();
                    videoInitialized = true;
                });
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
                catch (Exception ex) 
                {
                    Debug.WriteLine("StopCamera Exception: " + ex.Message);
                }
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
