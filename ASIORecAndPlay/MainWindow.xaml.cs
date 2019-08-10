// Simple program to route audio between ASIO devices
// Copyright(C) 2017-2019 LAGonauta

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.If not, see<http://www.gnu.org/licenses/>.

using AudioVUMeter;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace ASIORecAndPlay
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// Based on work by Mark Heath on NAudio ASIO PatchBay
  /// </summary>
  public partial class MainWindow : Window
  {
    private RecAndPlay asioRecAndPlay;
    private bool running;

    private System.Windows.Forms.NotifyIcon tray_icon;

    private void DispatchStatusText(object buffer)
    {
      Application.Current.Dispatcher.Invoke(() => UpdateText($"Buffered time: {((RecAndPlay)buffer).BufferedDuration().TotalMilliseconds.ToString()} ms."));
    }

    private void DispatchPlaybackMeters(object buffer)
    {
      Application.Current.Dispatcher.Invoke(() => UpdateMeter(((RecAndPlay)buffer).PlaybackAudioValue));
    }

    private void UpdateText(string message)
    {
      if (WindowState != WindowState.Minimized)
      {
        status_text.Text = message;
      }
    }

    private void UpdateMeter(VolumeMeterChannels values)
    {
      if (WindowState != WindowState.Minimized)
      {
        playBack_left.NewSampleValues(1, new VUValue[] { values.Left });
        playBack_right.NewSampleValues(1, new VUValue[] { values.Right });
        playBack_center.NewSampleValues(1, new VUValue[] { values.Center });
        playBack_bl.NewSampleValues(1, new VUValue[] { values.BackLeft });
        playBack_br.NewSampleValues(1, new VUValue[] { values.BackRight });
        playBack_sl.NewSampleValues(1, new VUValue[] { values.SideLeft });
        playBack_sr.NewSampleValues(1, new VUValue[] { values.SideRight });
        playBack_sw.NewSampleValues(1, new VUValue[] { values.Sub });
      }
    }

    private void UI_WasapiLatency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      UI_WasapiLatencyText.Text = $"Buffer size: {(sender as Slider).Value.ToString("F0")} ms";
    }

    public MainWindow()
    {
      InitializeComponent();

      var icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().ManifestModule.Name);

      tray_icon = new System.Windows.Forms.NotifyIcon()
      {
        Visible = true,
        Text = Title,
        Icon = icon
      };

      tray_icon.DoubleClick +=
        delegate (object sender, EventArgs e)
        {
          Show();
          WindowState = WindowState.Normal;
        };

      PopulateDevicesList();

      Closing += (sender, args) => Stop();
    }

    private void PopulateDevicesList()
    {
      PopulatePlaybackDevicesList();
      PopulateRecordingDeviceList();
    }

    private void PopulatePlaybackDevicesList()
    {
      UI_PlaybackDevices.Items.Clear();
      var IsAsioDriver = UI_AsioRadioButton.IsChecked.GetValueOrDefault(true);
      var playbackDeviceList = IsAsioDriver ? Asio.GetDevices() : Wasapi.Endpoints(DataFlow.Render, DeviceState.Active).Select(e => e.FriendlyName);
      foreach (var device in playbackDeviceList)
      {
        UI_PlaybackDevices.Items.Add(device);
      }

      if (UI_PlaybackDevices.Items.Count > 0)
      {
        UI_PlaybackDevices.SelectedIndex = 0;
      }
    }

    private void PopulateRecordingDeviceList()
    {
      UI_RecordDevices.Items.Clear();

      foreach (var device in Asio.GetDevices())
      {
        UI_RecordDevices.Items.Add(device);
      }

      if (UI_RecordDevices.Items.Count > 0)
      {
        UI_RecordDevices.SelectedIndex = 0;
      }
    }

    private void OnDeviceComboBoxStateChanged(object sender, SelectionChangedEventArgs e)
    {
      if (UI_ChannelMapping != null &&
        (sender == UI_RecordDevices || sender == UI_PlaybackDevices || sender == UI_WasapiChannelConfig))
      {
        UI_ChannelMapping.Children.Clear();
        List<string> input = new List<string>();
        if (UI_RecordDevices.SelectedItem != null)
        {
          input.Add("None");
          input.AddRange(Asio.GetChannelNames(UI_RecordDevices.SelectedItem.ToString(), ChannelType.Input));
        }

        string[] output = new string[0];
        if (UI_PlaybackDevices.SelectedItem != null)
        {
          if (UI_AsioRadioButton.IsChecked.GetValueOrDefault(true))
          {
            output = Asio.GetChannelNames(UI_PlaybackDevices.SelectedItem.ToString(), ChannelType.Output);
          }
          else
          {
            output = Wasapi.GetChannelNames((ChannelLayout)UI_WasapiChannelConfig.SelectedIndex);
          }
        }

        for (int i = 0; i < output.Length; ++i)
        {
          var text = new TextBlock { Text = output[i] };
          var comboBox = new ComboBox
          {
            ItemsSource = input,
            Margin = new Thickness { Bottom = 1, Top = 1, Left = 0, Right = 0 },
            SelectedIndex = input.Count > 1 ? i % (input.Count - 1) + 1 : 0
          };

          UI_ChannelMapping.Children.Add(text);
          UI_ChannelMapping.Children.Add(comboBox);
        }
      }
    }

    protected override void OnStateChanged(EventArgs e)
    {
      if (WindowState == WindowState.Minimized)
      {
        Hide();
        if (asioRecAndPlay != null)
        {
          asioRecAndPlay.CalculateRMS = false;
        }
      }
      else
      {
        if (asioRecAndPlay != null)
        {
          asioRecAndPlay.CalculateRMS = true;
        }
      }

      base.OnStateChanged(e);
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      tray_icon.Visible = false;
    }

    private void OnButtonCPClick(object sender, RoutedEventArgs e)
    {
      string device = string.Empty;
      if (sender == UI_AsioPlaybackControlPanel)
      {
        device = UI_PlaybackDevices.Text;
      }
      else if (sender == buttonRecCP)
      {
        device = UI_RecordDevices.Text;
      }

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

    private void OnPlaybackDriverChanged(object sender, RoutedEventArgs e)
    {
      if (UI_AsioRadioButton.IsChecked.GetValueOrDefault(true))
      {
        UI_AsioPlayBackSettings.Visibility = Visibility.Visible;
        UI_WasapiPlaybackSettings.Visibility = Visibility.Collapsed;
        UI_WasapiChannelConfigPanel.Visibility = Visibility.Collapsed;
        Grid.SetColumnSpan(UI_PlaybackDeviceCombobox, 2);
      }
      else
      {
        UI_AsioPlayBackSettings.Visibility = Visibility.Collapsed;
        UI_WasapiPlaybackSettings.Visibility = Visibility.Visible;
        UI_WasapiChannelConfigPanel.Visibility = Visibility.Visible;
        Grid.SetColumnSpan(UI_PlaybackDeviceCombobox, 1);
      }
      PopulatePlaybackDevicesList();
    }

    private Timer statusTextTimer;
    private Timer audioMeterTimer;

    private void OnButtonBeginClick(object sender, RoutedEventArgs e)
    {
      if (!running)
      {
        if (UI_AsioRadioButton.IsChecked.GetValueOrDefault(true) == false || UI_RecordDevices.SelectedIndex != UI_PlaybackDevices.SelectedIndex)
        {
          running = true;
          var mapping = new ChannelMapping();
          {
            int outputChannel = 0;
            foreach (var inputBox in UI_ChannelMapping.Children.OfType<ComboBox>())
            {
              if (inputBox.SelectedIndex > 0)
              {
                mapping.Add(inputBox.SelectedIndex - 1, outputChannel);
              }

              ++outputChannel;
            }
          }

          asioRecAndPlay = new RecAndPlay(
            new AsioOut(UI_RecordDevices.Text),

            UI_AsioRadioButton.IsChecked.GetValueOrDefault(true) ?
            new AsioOut(UI_PlaybackDevices.Text)
            : (IWavePlayer)new WasapiOut(
              Wasapi.Endpoints(DataFlow.Render, DeviceState.Active).First(endpoint => endpoint.FriendlyName == UI_PlaybackDevices.Text),
              UI_WasapiExclusiveMode.IsChecked.GetValueOrDefault(true) ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared,
              UI_WasapiPullMode.IsChecked.GetValueOrDefault(true),
              (int)UI_WasapiLatency.Value),

            mapping,
            UI_WasapiChannelConfig.IsVisible ? (ChannelLayout?)UI_WasapiChannelConfig.SelectedIndex : null);

          BatchChangeElements(false);

          UI_ButtonBegin.Content = "Stop";

          asioRecAndPlay.CalculateRMS = true;
          asioRecAndPlay.Play();

          statusTextTimer = new Timer(new TimerCallback(DispatchStatusText), asioRecAndPlay, 0, 1000);
          audioMeterTimer = new Timer(new TimerCallback(DispatchPlaybackMeters), asioRecAndPlay, 0, 300);
        }
        else
        {
          // When using the same ASIO device we must use other type of logic, which is not implemented here.
          // The basis of this program, Mark Heath's NAudio ASIO PatchBay, has a proper solution for that.
          MessageBox.Show("ASIO devices must not be the same");
        }
      }
      else
      {
        statusTextTimer.Dispose();
        audioMeterTimer.Dispose();
        Application.Current.Dispatcher.Invoke(() => UpdateText("Stopped."));
        Stop();
      }
    }

    private void Stop()
    {
      if (running)
      {
        statusTextTimer.Dispose();
        Application.Current.Dispatcher.Invoke(() => UpdateText("Stopped."));

        audioMeterTimer.Dispose();
        Application.Current.Dispatcher.Invoke(() => UpdateMeter(new VolumeMeterChannels()));

        running = false;
        UI_ButtonBegin.Content = "Start";
        BatchChangeElements(true);

        asioRecAndPlay.Dispose();
      }
    }

    private void BatchChangeElements(bool value)
    {
      UI_RecordDevices.IsEnabled = value;
      UI_PlaybackDeviceSelection.IsEnabled = value;
      UI_ChannelMappingBox.IsEnabled = value;
      UI_PlaybackDriver.IsEnabled = value;
      UI_WasapiPlaybackSettings.IsEnabled = value;
      UI_WasapiChannelConfigPanel.IsEnabled = value;
    }
  }
}