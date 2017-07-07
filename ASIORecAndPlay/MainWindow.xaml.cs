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
using System.Windows;
using NAudio.Wave;
using System.Diagnostics;
using System.Windows.Controls;

namespace ASIORecAndPlay
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// Based on work by Mark Heath on NAudio ASIO PatchBay
  /// </summary>
  public partial class MainWindow : Window
  {
    private AsioOut asio_rec;
    private AsioOut asio_play;
    private BufferedWaveProvider buffer;
    private float[] samples = new float[1024 * 1024];
    private float[] samples_correct = new float[1024 * 1024];
    private byte[] byte_samples = new byte[1024 * 1024 * 4];
    private int channels = 2;
    private bool running;

    System.Windows.Forms.NotifyIcon tray_icon;

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
          this.Show();
          this.WindowState = WindowState.Normal;
        };

      foreach (var device in AsioOut.GetDriverNames())
      {
        comboAsioRecordDevices.Items.Add(device);
        comboAsioPlayDevices.Items.Add(device);
      }

      if (comboAsioRecordDevices.Items.Count > 0)
        comboAsioRecordDevices.SelectedIndex = 0;

      if (comboAsioPlayDevices.Items.Count > 0)
        comboAsioPlayDevices.SelectedIndex = 0;

      Closing += (sender, args) => Stop();
    }

    protected override void OnStateChanged(EventArgs e)
    {
      if (WindowState == WindowState.Minimized)
      {
        this.Hide();
      }

      base.OnStateChanged(e);
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      tray_icon.Visible = false;
    }

    private void OnButtonCPClick(object sender, RoutedEventArgs e)
    {
      if (sender == buttonPlayCP)
      {
        if (asio_play != null)
        {
          asio_play.ShowControlPanel();
        }
      }
      else if (sender == buttonRecCP)
      {
        if (asio_rec != null)
        {
          asio_rec.ShowControlPanel();
        }
      }
    }

    private void OnButtonBeginClick(object sender, RoutedEventArgs e)
    {
      if (!running)
      {
        if (comboAsioRecordDevices.SelectedIndex != comboAsioPlayDevices.SelectedIndex)
        {
          running = true;
          asio_rec = new AsioOut((string)comboAsioRecordDevices.SelectedItem);
          asio_play = new AsioOut((string)comboAsioPlayDevices.SelectedItem);

          comboAsioRecordDevices.IsEnabled = false;
          comboAsioPlayDevices.IsEnabled = false;

          stackRecChannels.Children.Clear();
          for (int i = 0; i < asio_rec.DriverInputChannelCount; ++i)
          {
            TextBlock temp = new TextBlock();
            temp.Text = asio_rec.AsioInputChannelName(i);
            stackRecChannels.Children.Add(temp);
          }
          buttonRecCP.IsEnabled = true;

          stackPlayChannels.Children.Clear();
          for (int i = 0; i < asio_play.DriverOutputChannelCount; ++i)
          {
            TextBlock temp = new TextBlock();
            temp.Text = asio_play.AsioOutputChannelName(i);
            stackPlayChannels.Children.Add(temp);
          }
          buttonPlayCP.IsEnabled = true;

          if (comboChannelConfig.SelectedIndex == 2)
          {
            channels = 6;
          }
          else if (comboChannelConfig.SelectedIndex == 3)
          {
            channels = 8;
          }
          else
          {
            channels = 2;
          }
          comboChannelConfig.IsEnabled = false;

          NAudio.Wave.WaveFormat format = new NAudio.Wave.WaveFormat(48000, 32, channels);
          buffer = new NAudio.Wave.BufferedWaveProvider(format);

          asio_rec.InitRecordAndPlayback(null, channels, 48000);
          asio_rec.AudioAvailable += new EventHandler<NAudio.Wave.AsioAudioAvailableEventArgs>(OnAudioAvailable);

          asio_play.Init(buffer);

          asio_rec.Play();
          asio_play.Play();

          buttonBegin.Content = "Stop";
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
        Stop();
      }
    }

    private void Stop()
    {
      if (running)
      {
        asio_play.Stop();
        asio_play.Dispose();
        asio_play = null;
        buttonPlayCP.IsEnabled = false;
        stackPlayChannels.Children.Clear();

        asio_rec.Stop();
        asio_rec.Dispose();
        asio_rec = null;
        buttonRecCP.IsEnabled = false;
        stackRecChannels.Children.Clear();

        running = false;
        buttonBegin.Content = "Begin";
        comboChannelConfig.IsEnabled = true;
        comboAsioRecordDevices.IsEnabled = true;
        comboAsioPlayDevices.IsEnabled = true;
      }
    }

    void OnAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
    {
      e.GetAsInterleavedSamples(samples);

      // Correct channel mapping
      if (channels == 6)
      {
        for (int i = 0; i < e.SamplesPerBuffer; ++i)
        {
          samples_correct[i * channels + 0 /*FRONT LEFT*/] =
              samples[i * channels + 0 /*FRONT LEFT*/];
          samples_correct[i * channels + 1 /*FRONT RIGHT*/] =
              samples[i * channels + 1 /*FRONT RIGHT*/];
          samples_correct[i * channels + 2 /*FRONT CENTER*/] =
              samples[i * channels + 4 /*FRONT CENTER*/];
          samples_correct[i * channels + 3 /*sub/lfe*/] =
              samples[i * channels + 5 /*sub/lfe*/];
          samples_correct[i * channels + 4 /*REAR LEFT*/] =
              samples[i * channels + 2 /*REAR LEFT*/];
          samples_correct[i * channels + 5 /*REAR RIGHT*/] =
              samples[i * channels + 3 /*REAR RIGHT*/];
        }

        Buffer.BlockCopy(samples_correct, 0, byte_samples, 0, e.SamplesPerBuffer * channels * sizeof(float));
      }
      else if (channels == 8)
      {
        for (int i = 0; i < e.SamplesPerBuffer; ++i)
        {
          samples_correct[i * channels + 0 /*FRONT LEFT*/] =
              samples[i * channels + 0 /*FRONT LEFT*/];
          samples_correct[i * channels + 1 /*FRONT RIGHT*/] =
              samples[i * channels + 1 /*FRONT RIGHT*/];
          samples_correct[i * channels + 2 /*FRONT CENTER*/] =
              samples[i * channels + 4 /*FRONT CENTER*/];
          samples_correct[i * channels + 3 /*sub/lfe*/] =
              samples[i * channels + 5 /*sub/lfe*/];
          samples_correct[i * channels + 4 /*REAR LEFT*/] =
              samples[i * channels + 2 /*REAR LEFT*/];
          samples_correct[i * channels + 5 /*REAR RIGHT*/] =
              samples[i * channels + 3 /*REAR RIGHT*/];

          // Not sure about these two
          samples_correct[i * channels + 6 /*SIDE LEFT*/] =
              samples[i * channels + 6 /*SIDE LEFT*/];
          samples_correct[i * channels + 7 /*SIDE RIGHT*/] =
              samples[i * channels + 7 /*SIDE RIGHT*/];
        }

        Buffer.BlockCopy(samples_correct, 0, byte_samples, 0, e.SamplesPerBuffer * channels * sizeof(float));
      }
      else
      {
        Buffer.BlockCopy(samples, 0, byte_samples, 0, e.SamplesPerBuffer * channels * sizeof(float));
      }

      buffer.AddSamples(byte_samples, 0, e.SamplesPerBuffer * channels * sizeof(float));
      //Trace.WriteLine(buffer.BufferedDuration);
    }
  }
}
