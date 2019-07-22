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

using System;
using System.Collections.Generic;
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

    public delegate void UpdateStatusTextCallback(string message);

    private void DispatchStatusText(object buffer)
    {
      string message = $"Buffered time: {((RecAndPlay)buffer).BufferedDuration().TotalMilliseconds.ToString()} ms.";
      status_text.Dispatcher.Invoke(new UpdateStatusTextCallback(UpdateText),
        new object[] { message });
    }

    private void UpdateText(string message)
    {
      status_text.Text = message;
    }

    public MainWindow()
    {
      InitializeComponent();

      var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().ManifestModule.Name);

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

          buttonBegin.Content = "Stop";

          asioRecAndPlay.SetChannelMapping(new Dictionary<uint, uint>
          {
            { 0, 0 },
            { 1, 1 }
          });
          asioRecAndPlay.Play();

          status_text_timer = new Timer(new TimerCallback(DispatchStatusText), asioRecAndPlay, 0, 1000);
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
        status_text.Dispatcher.Invoke(new UpdateStatusTextCallback(UpdateText),
          new object[] { "Stopped." });
        Stop();
      }
    }

    private void Stop()
    {
      if (running)
      {
        status_text_timer.Dispose();
        status_text.Dispatcher.Invoke(new UpdateStatusTextCallback(UpdateText),
          new object[] { "Stopped." });

        stackPlayChannels.Children.Clear();
        stackRecChannels.Children.Clear();

        running = false;
        buttonBegin.Content = "Start";
        comboRecordingChannelConfig.IsEnabled = true;
        comboAsioRecordDevices.IsEnabled = true;
        comboAsioPlayDevices.IsEnabled = true;
        asioRecAndPlay.Dispose();
      }
    }
  }
}