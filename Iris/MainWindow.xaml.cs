using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;

namespace Iris;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Our two main objects —
    // MpvHost creates the native child window (HWND) inside WPF
    // MpvPlayer wraps libmpv and renders video into that HWND
    private MpvHost _mpvHost;
    private MpvPlayer? _mpvPlayer;
    private bool _isPaused = false;
    private bool _isMuted = false;
    
    private System.Windows.Threading.DispatcherTimer? _timer;
    private bool _isSeeking = false;
    
    public MainWindow()
    {
        InitializeComponent();
        TitleBar.MouseLeftButtonDown += (s,e) => DragMove();
        BtnMinimize.Click += (s, e) => WindowState = WindowState.Minimized;
        BtnMaximize.Click += (s, e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BtnClose.Click += (s,e) => Application.Current.Shutdown();
        
        _mpvHost = new MpvHost();
        VideoArea.Content = _mpvHost;
        
       

        DragEnter += (s, e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        Drop += (s, e) =>
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files?.Length > 0)
            {
                _mpvPlayer?.Load(files[0]);
                _isPaused = false;
                IconPlayPause.Source = new Uri("Assets/Icons/pause.svg", UriKind.Relative);
                
                TitleBarText.Text = System.IO.Path.GetFileNameWithoutExtension(files[0]);
            }
        };

        BtnPlayPause.Click += (s, e) =>
        {
            if(_mpvPlayer == null) return;
            if(_mpvPlayer == null || _mpvPlayer.Duration ==0) return;
            
            // If video ended, replay from start
            if (_mpvPlayer.CurrentTime >= _mpvPlayer.Duration - 0.5)
            {
                _mpvPlayer.SeekAbsolute(0);
                _mpvPlayer.Play();
                _isPaused = false;
                IconPlayPause.Source = new Uri("Assets/Icons/pause.svg", UriKind.Relative);
                return;
            }
    
            _mpvPlayer?.TogglePlayPause();
            _isPaused = !_isPaused;
            
            IconPlayPause.Source = _isPaused
                ? new Uri("Assets/Icons/play.svg", UriKind.Relative)
                : new Uri("Assets/Icons/pause.svg", UriKind.Relative);
        };
        
        BtnBack.Click += (s,e) => _mpvPlayer?.Seek(-5);
        BtnForward.Click += (s,e) => _mpvPlayer?.Seek(5);
        
        BtnMute.Click += (s, e) =>
        {
            if (_mpvPlayer == null) return;
            // toggle mute via mpv command
            _mpvPlayer.ToggleMute();
            _isMuted = !_isMuted;
            
            IconVolume.Source = _isMuted
                ? new Uri("Assets/Icons/mute.svg", UriKind.Relative)
                : new Uri("Assets/Icons/volume.svg", UriKind.Relative);
        };
        
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Now the HWND is guaranteed to exist — safe to initialize mpv
        _mpvPlayer = new MpvPlayer(_mpvHost.Hwnd);

        _timer = new System.Windows.Threading.DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += OnTimerTick;
        _timer.Start();
        
        SeekBar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler((s, e) => _isSeeking = true));

        SeekBar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) =>
            {
                _mpvPlayer?.SeekAbsolute(SeekBar.Value);
                _isSeeking = false;
            }));

        // Handle click to seek
        SeekBar.PreviewMouseLeftButtonDown += (s, e) => _isSeeking = true;
        SeekBar.PreviewMouseLeftButtonUp += (s, e) =>
        {
            _mpvPlayer?.SeekAbsolute(SeekBar.Value);
            _isSeeking = false;
        };
    }
    
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        this.Padding = WindowState == WindowState.Minimized ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _mpvPlayer?.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_mpvPlayer == null || _isSeeking) return;

        var duration = _mpvPlayer.Duration;
        var current  = _mpvPlayer.CurrentTime;

        if (duration > 0)
        {
            SeekBar.Maximum = duration;
            SeekBar.Value   = current;

            if (current >= duration - 0.5)
            {
                _isPaused = true;
                IconPlayPause.Source = new Uri("Assets/Icons/play.svg", UriKind.Relative);
            }
        }

        TxtCurrentTime.Text = TimeSpan.FromSeconds(current).ToString(@"h\:mm\:ss");
        TxtDuration.Text    = TimeSpan.FromSeconds(duration).ToString(@"h\:mm\:ss");
    }
}