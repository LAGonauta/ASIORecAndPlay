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

using ASIORecAndPlay.ViewModel;
using AudioVUMeter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
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
    private System.Windows.Forms.NotifyIcon tray_icon;

    private void DispatchPlaybackMeters(object buffer)
    {
      Application.Current.Dispatcher.Invoke(() => UpdateMeter(((RecAndPlay)buffer).PlaybackAudioValue));
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

    public MainWindow()
    {
      InitializeComponent();
      var indexes = UI_ChannelMapping.Children.OfType<ComboBox>().Select(x => x.SelectedIndex);
      DataContext = new MainWindowViewModel(indexes); // FIXME

      var icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().ManifestModule.Name);

      tray_icon = new System.Windows.Forms.NotifyIcon()
      {
        Visible = true,
        Text = Title,
        Icon = icon
      };

      tray_icon.DoubleClick += (sender, e) =>
      {
        Show();
        WindowState = WindowState.Normal;
      };
      Closing += (sender, args) =>
      {
        var context = (MainWindowViewModel)DataContext;
        context.Stop();
      };
      Loaded += (sender, args) =>
      {
        var context = (MainWindowViewModel)DataContext;
        context.OnLoaded();
      };
    }

    private void OnDeviceComboBoxStateChanged(object sender, SelectionChangedEventArgs e)
    {
      if (UI_ChannelMapping != null &&
        (sender == UI_RecordDevices || sender == UI_PlaybackDevices || sender == UI_WasapiChannelConfig))
      {
        UI_ChannelMapping.Children.Clear();
        List<string> input = new();
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
            output = Wasapi.GetChannelNames((ChannelLayout)UI_WasapiChannelConfig.SelectedItem);
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
      }

      var context = (MainWindowViewModel)DataContext;
      context.OnWindowStateChanged(WindowState);

      base.OnStateChanged(e);
    }

    private void Window_Closing(object sender, EventArgs e)
    {
      tray_icon.Visible = false;
    }

    private void OnPlaybackDriverChanged(object sender, RoutedEventArgs e)
    {
      var context = (MainWindowViewModel)DataContext;
      if (UI_AsioRadioButton.IsChecked.GetValueOrDefault(true))
      {
        context.UseAsio = true;
        Grid.SetColumnSpan(UI_PlaybackDeviceCombobox, 2);
      }
      else
      {
        context.UseAsio = false;
        Grid.SetColumnSpan(UI_PlaybackDeviceCombobox, 1);
      }
      context.OnPlaybackDriverChanged.Execute(Unit.Default);
    }
  }
}
