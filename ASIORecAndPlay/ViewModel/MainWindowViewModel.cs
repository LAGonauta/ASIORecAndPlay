using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ReactiveUI;

namespace ASIORecAndPlay.ViewModel
{
  internal class MainWindowViewModel : BaseViewModel
  {
    private IEnumerable<int> _inputChannelIndexes = new List<int>(); // FIXME

    private bool _running = false;
    public bool IsRunning
    {
      get
      {
        return _running;
      }

      set
      {
        if (_running != value)
        {
          this.RaisePropertyChanging(nameof(IsRunning));
          this.RaisePropertyChanging(nameof(IsNotRunning));
          _running = value;
          this.RaisePropertyChanged(nameof(IsRunning));
          this.RaisePropertyChanged(nameof(IsNotRunning));
        }
      }
    }
    public bool IsNotRunning { get => !_running; }


    private RecAndPlay asioRecAndPlay;

    private bool _useAsio = true;
    public bool UseAsio
    {
      get
      {
        return _useAsio;
      }

      set
      {
        if (_useAsio != value)
        {
          this.RaisePropertyChanging(nameof(UseAsio));
          this.RaisePropertyChanging(nameof(NotUseAsio));
          _useAsio = value;
          this.RaisePropertyChanged(nameof(UseAsio));
          this.RaisePropertyChanged(nameof(NotUseAsio));
        }
      }
    }
    public bool NotUseAsio { get => !_useAsio; }

    public ObservableCollection<ChannelLayout> _wasapiChannelsLayouts = new()
    {
      ChannelLayout.Mono,
      ChannelLayout.Stereo,
      ChannelLayout.Quad,
      ChannelLayout.Surround51,
      ChannelLayout.Surround71
    };

    public ObservableCollection<ChannelLayout> WasapiChannelsLayouts {get => _wasapiChannelsLayouts;}

    private ChannelLayout _wasapiChannelLayout = ChannelLayout.Mono;
    public ChannelLayout WasapiChannelLayout { get => _wasapiChannelLayout; set => this.RaiseAndSetIfChanged(ref _wasapiChannelLayout, value); }

    private bool _wasapiExclusiveMode = true;
    public bool WasapiExclusiveMode { get => _wasapiExclusiveMode; set => this.RaiseAndSetIfChanged(ref _wasapiExclusiveMode, value); }

    private bool _wasapiPullMode = true;
    public bool WasapiPullMode { get => _wasapiPullMode; set => this.RaiseAndSetIfChanged(ref _wasapiPullMode, value); }

    private string _wasapiLatencyText = "10";
    public string WasapiLatencyText { get => _wasapiLatencyText; set => this.RaiseAndSetIfChanged(ref _wasapiLatencyText, value); }

    private double _wasapiLatency = 10;
    public double WasapiLatency { get => _wasapiLatency; set => this.RaiseAndSetIfChanged(ref _wasapiLatency, value); }


    private string _statusText = "";
    public string StatusText { get => _statusText; set => this.RaiseAndSetIfChanged(ref _statusText, value); }

    private string _buttonBeginText = "Start";
    public string ButtonBeginContent { get => _buttonBeginText; set => this.RaiseAndSetIfChanged(ref _buttonBeginText, value); }

    public ObservableCollection<string> PlaybackDevices { get; } = new ObservableCollection<string>();

    private int _selectedPlaybackDeviceIndex = 0;
    public int PlaybackDeviceSelectedIndex { get => _selectedPlaybackDeviceIndex; set => this.RaiseAndSetIfChanged(ref _selectedPlaybackDeviceIndex, value); }

    public ObservableCollection<string> RecordingDevices { get; } = new ObservableCollection<string>();
    private int _selectedRecordingDeviceIndex = 0;
    public int RecordingDeviceSelectedIndex { get => _selectedRecordingDeviceIndex; set => this.RaiseAndSetIfChanged(ref _selectedRecordingDeviceIndex, value); }

    private double _bufferedTime = 0;
    public double BufferedTime { get => _bufferedTime; set => this.RaiseAndSetIfChanged(ref _bufferedTime, value); }

    private Timer _bufferedTimeTimer;

    public ICommand OnAsioPlaybackControlPanel { get; init; }
    public ICommand OnAsioRecordingControlPanel { get; init; }
    public ICommand OnBeginRouting { get; init; }
    public ICommand OnUpdateChannelMappingMatrix { get; init; }
    public ICommand OnPlaybackDriverChanged { get; init; }

    public MainWindowViewModel(IEnumerable<int> inputChannelIndexes)
    {
      this.WhenValueChanged(o => o.BufferedTime)
        .Subscribe(bufferedTime => StatusText = $"Buffered time: {bufferedTime} ms.");
      this.WhenValueChanged(o => o.WasapiLatency)
        .Subscribe(wasapiLatency => WasapiLatencyText = $"Buffer size: {wasapiLatency:F0} ms");

      OnAsioPlaybackControlPanel = ReactiveCommand.Create(() =>
      {
        OpenAsioControlPanel(PlaybackDevices[PlaybackDeviceSelectedIndex]);
      });
      OnAsioRecordingControlPanel = ReactiveCommand.Create(() =>
      {
        OpenAsioControlPanel(RecordingDevices[RecordingDeviceSelectedIndex]);
      });
      OnBeginRouting = ReactiveCommand.Create(BeginRouting);
      OnUpdateChannelMappingMatrix = ReactiveCommand.Create(UpdateChannelMappingMatrix);
      OnPlaybackDriverChanged = ReactiveCommand.Create(UpdatePlaybackDriver);
      _inputChannelIndexes = inputChannelIndexes;
    }

    private void UpdatePlaybackDriver()
    {
      PopulateDevicesList();
    }

    private void UpdateChannelMappingMatrix()
    {

    }

    private void OpenAsioControlPanel(string device)
    {
      if (!string.IsNullOrWhiteSpace(device))
      {
        if (asioRecAndPlay != null && asioRecAndPlay.Valid)
        {
          asioRecAndPlay.ShowControlPanel(device);
        }
        else
        {
          Asio.ShowControlPanel(device);
        }
      }
    }

    public void OnLoaded()
    {
      PopulateDevicesList();
    }

    public void OnWindowStateChanged(WindowState state)
    {
      if (asioRecAndPlay != null)
      {
        asioRecAndPlay.CalculateRMS = state != WindowState.Minimized;
      }
    }

    private void BeginRouting()
    {
      if (!IsRunning)
      {
        if (!UseAsio || PlaybackDeviceSelectedIndex != RecordingDeviceSelectedIndex)
        {
          IsRunning = true;
          var mapping = new ChannelMapping();
          {
            int outputChannel = 0;
            foreach (var selectedIndex in _inputChannelIndexes)
            {
              if (selectedIndex > 0)
              {
                mapping.Add(selectedIndex - 1, outputChannel);
              }

              ++outputChannel;
            }
          }

          asioRecAndPlay = new RecAndPlay(
            new AsioOut(RecordingDevices[RecordingDeviceSelectedIndex]),
            UseAsio ?
            new AsioOut(PlaybackDevices[PlaybackDeviceSelectedIndex])
            : (IWavePlayer)new WasapiOut(
              Wasapi.Endpoints(DataFlow.Render, DeviceState.Active).First(endpoint => endpoint.FriendlyName == PlaybackDevices[PlaybackDeviceSelectedIndex]),
              WasapiExclusiveMode ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared,
              WasapiPullMode,
              (int)WasapiLatency),
            mapping,
            UseAsio ? null : WasapiChannelLayout,
            () => { BeginRouting(); UpdatePlaybackDriver(); });

          ButtonBeginContent = "Stop";

          asioRecAndPlay.CalculateRMS = true;
          asioRecAndPlay.Play();

          _bufferedTimeTimer = new Timer(o =>
          {
            var rp = asioRecAndPlay;
            if (rp != null)
            {
              BufferedTime = rp.BufferedDuration().TotalMilliseconds;
            }
          }, null, 0, 1000);
          //audioMeterTimer = new Timer(new TimerCallback(DispatchPlaybackMeters), asioRecAndPlay, 0, 300); FIXME
        }
        else
        {
          // When using the same ASIO device we must use other type of logic, which is not implemented here.
          // The basis of this program, Mark Heath's NAudio ASIO PatchBay, has a proper solution for that.
          MessageBox.Show("ASIO devices must not be the same", "Problem starting playback");
        }
      }
      else
      {
        Stop();
      }
    }

    public void Stop()
    {
      if (IsRunning)
      {
        _bufferedTimeTimer.Dispose();
        StatusText = "Stopped.";

        //audioMeterTimer.Dispose();
        //Application.Current.Dispatcher.Invoke(() => UpdateMeter(new VolumeMeterChannels()));

        IsRunning = false;
        ButtonBeginContent = "Start";

        asioRecAndPlay.Dispose();
      }
    }

    private void PopulateDevicesList()
    {
      PopulatePlaybackDevicesList();
      PopulateRecordingDeviceList();
    }

    private void PopulatePlaybackDevicesList()
    {
      PlaybackDevices.Clear();
      var playbackDeviceList = UseAsio ?
        Asio.GetDevices() :
        Wasapi.Endpoints(DataFlow.Render, DeviceState.Active).Select(e => e.FriendlyName);

      PlaybackDevices.AddRange(playbackDeviceList);
      if (PlaybackDevices.Count > 0)
      {
        PlaybackDeviceSelectedIndex = 0;
      }
    }

    private void PopulateRecordingDeviceList()
    {
      RecordingDevices.Clear();
      RecordingDevices.AddRange(Asio.GetDevices());

      if (RecordingDevices.Count > 0)
      {
        RecordingDeviceSelectedIndex = 0;
      }
    }
  }
}
