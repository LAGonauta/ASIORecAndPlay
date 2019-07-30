// Simple program to route audio between ASIO devices
// Copyright(C) 2017  LAGonauta

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
using System;
using System.Collections.Generic;
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
      status_text.Dispatcher.Invoke(() => UpdateText($"Buffered time: {((RecAndPlay)buffer).BufferedDuration().TotalMilliseconds.ToString()} ms."));
    }

    private void DispatchPlaybackMeters(object buffer)
    {
      playBack_left.Dispatcher.Invoke(() => UpdateMeter(((RecAndPlay)buffer).PlaybackAudioValue));
    }

    private void UpdateText(string message)
    {
      status_text.Text = message;
    }

    private void UpdateMeter(VolumeMeterChannels values)
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

      foreach (var device in Asio.GetDevices())
      {
        comboAsioRecordDevices.Items.Add(device);
        comboAsioPlayDevices.Items.Add(device);
      }

      if (comboAsioRecordDevices.Items.Count > 0)
      {
        comboAsioRecordDevices.SelectedIndex = 0;
      }

      if (comboAsioPlayDevices.Items.Count > 0)
      {
        comboAsioPlayDevices.SelectedIndex = 0;
      }

      Closing += (sender, args) => Stop();
    }

    protected override void OnStateChanged(EventArgs e)
    {
      if (WindowState == WindowState.Minimized)
      {
        Hide();
        asioRecAndPlay.CalculateRMS = false;
      }
      else
      {
        asioRecAndPlay.CalculateRMS = true;
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
      if (sender == buttonPlayCP)
      {
        device = comboAsioPlayDevices.Text;
      }
      else if (sender == buttonRecCP)
      {
        device = comboAsioRecordDevices.Text;
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

    private Timer status_text_timer;
    private Timer audio_meter_timer;

    private void OnButtonBeginClick(object sender, RoutedEventArgs e)
    {
      if (!running)
      {
        if (comboAsioRecordDevices.SelectedIndex != comboAsioPlayDevices.SelectedIndex)
        {
          running = true;
          var channels = 2;
          if (comboRecordingChannelConfig.SelectedIndex == 2)
          {
            channels = 6;
          }
          else if (comboRecordingChannelConfig.SelectedIndex == 3)
          {
            channels = 8;
          }
          asioRecAndPlay = new RecAndPlay(comboAsioRecordDevices.Text, channels, comboAsioPlayDevices.Text, channels);

          comboAsioRecordDevices.IsEnabled = false;
          comboAsioPlayDevices.IsEnabled = false;

          stackRecChannels.Children.Clear();
          {
            var channelNames = asioRecAndPlay.GetRecordingDeviceChannelsNames();
            foreach (var channel in channelNames)
            {
              TextBlock temp = new TextBlock();
              temp.Text = channel;
              stackRecChannels.Children.Add(temp);
            }
          }

          stackPlayChannels.Children.Clear();
          {
            var channelNames = asioRecAndPlay.GetPlaybackDeviceChannelsNames();
            foreach (var channel in channelNames)
            {
              TextBlock temp = new TextBlock();
              temp.Text = channel;
              stackPlayChannels.Children.Add(temp);
            }
          }
          comboRecordingChannelConfig.IsEnabled = false;
          comboPlaybackChannelConfig.IsEnabled = false;

          buttonBegin.Content = "Stop";

          asioRecAndPlay.SetChannelMapping(new ChannelMapping(new Dictionary<uint, uint>
          {
            { 0, 0 },
            { 1, 1 },
            //{ 2, 4 },
            //{ 3, 5 },
            //{ 4, 2 },
            //{ 5, 3 },
            //{ 6, 6 },
            //{ 7, 7 },
          }));
          asioRecAndPlay.CalculateRMS = true;
          asioRecAndPlay.Play();

          status_text_timer = new Timer(new TimerCallback(DispatchStatusText), asioRecAndPlay, 0, 1000);
          audio_meter_timer = new Timer(new TimerCallback(DispatchPlaybackMeters), asioRecAndPlay, 0, 300);
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
        status_text_timer.Dispose();
        audio_meter_timer.Dispose();
        status_text.Dispatcher.Invoke(() => UpdateText("Stopped."));
        Stop();
      }
    }

    private void Stop()
    {
      if (running)
      {
        status_text_timer.Dispose();
        status_text.Dispatcher.Invoke(() => UpdateText("Stopped."));

        audio_meter_timer.Dispose();
        playBack_left.Dispatcher.Invoke(() => UpdateMeter(new VolumeMeterChannels()));

        stackPlayChannels.Children.Clear();
        stackRecChannels.Children.Clear();

        running = false;
        buttonBegin.Content = "Start";
        comboRecordingChannelConfig.IsEnabled = true;
        comboPlaybackChannelConfig.IsEnabled = true;
        comboAsioRecordDevices.IsEnabled = true;
        comboAsioPlayDevices.IsEnabled = true;
        asioRecAndPlay.Dispose();
      }
    }
  }
}