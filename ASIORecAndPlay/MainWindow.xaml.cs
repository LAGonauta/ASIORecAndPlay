﻿// Simple program to route audio between ASIO devices
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
        private AsioOut asioRec;
        private AsioOut asioPlay;
        private BufferedWaveProvider buffer;
        private float[] samples = new float[1024 * 1024];
        private float[] samplesCorrect = new float[1024 * 1024];
        private byte[] byteSamples = new byte[1024 * 1024 * 4];
        private int channels = 2;
        private bool running;

        public MainWindow()
        {
            InitializeComponent();
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

        private void OnButtonCPClick(object sender, RoutedEventArgs e)
        {
            if (sender == buttonPlayCP)
            {
                if (asioPlay != null)
                {
                    asioPlay.ShowControlPanel();
                }
            }
            else if (sender == buttonRecCP)
            {
                if (asioRec != null)
                {
                    asioRec.ShowControlPanel();
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
                    asioRec = new AsioOut((string)comboAsioRecordDevices.SelectedItem);
                    asioPlay = new AsioOut((string)comboAsioPlayDevices.SelectedItem);

                    stackRecChannels.Children.Clear();
                    for (int i = 0; i < asioRec.DriverInputChannelCount; ++i)
                    {
                        TextBlock temp = new TextBlock();
                        temp.Text = asioRec.AsioInputChannelName(i);
                        stackRecChannels.Children.Add(temp);
                    }
                    buttonRecCP.IsEnabled = true;

                    stackPlayChannels.Children.Clear();
                    for (int i = 0; i < asioPlay.DriverOutputChannelCount; ++i)
                    {
                        TextBlock temp = new TextBlock();
                        temp.Text = asioPlay.AsioOutputChannelName(i);
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

                    asioRec.InitRecordAndPlayback(null, channels, 48000);
                    asioRec.AudioAvailable += new EventHandler<NAudio.Wave.AsioAudioAvailableEventArgs>(OnAudioAvailable);

                    asioPlay.Init(buffer);

                    asioRec.Play();
                    asioPlay.Play();

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
                asioPlay.Stop();
                asioPlay.Dispose();
                asioPlay = null;
                buttonPlayCP.IsEnabled = false;
                stackPlayChannels.Children.Clear();

                asioRec.Stop();
                asioRec.Dispose();
                asioRec = null;
                buttonRecCP.IsEnabled = false;
                stackRecChannels.Children.Clear();

                running = false;
                buttonBegin.Content = "Begin";
                comboChannelConfig.IsEnabled = true;
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
                    samplesCorrect[i * channels + 0 /*FRONT LEFT*/] =
                        samples[i * channels + 0 /*FRONT LEFT*/];
                    samplesCorrect[i * channels + 1 /*FRONT RIGHT*/] =
                        samples[i * channels + 1 /*FRONT RIGHT*/];
                    samplesCorrect[i * channels + 2 /*FRONT CENTER*/] =
                        samples[i * channels + 4 /*FRONT CENTER*/];
                    samplesCorrect[i * channels + 3 /*sub/lfe*/] =
                        samples[i * channels + 5 /*sub/lfe*/];
                    samplesCorrect[i * channels + 4 /*REAR LEFT*/] =
                        samples[i * channels + 2 /*REAR LEFT*/];
                    samplesCorrect[i * channels + 5 /*REAR RIGHT*/] =
                        samples[i * channels + 3 /*REAR RIGHT*/];
                }

                Buffer.BlockCopy(samplesCorrect, 0, byteSamples, 0, e.SamplesPerBuffer * channels * sizeof(float));
            }
            else if (channels == 8)
            {
                for (int i = 0; i < e.SamplesPerBuffer; ++i)
                {
                    samplesCorrect[i * channels + 0 /*FRONT LEFT*/] =
                        samples[i * channels + 0 /*FRONT LEFT*/];
                    samplesCorrect[i * channels + 1 /*FRONT RIGHT*/] =
                        samples[i * channels + 1 /*FRONT RIGHT*/];
                    samplesCorrect[i * channels + 2 /*FRONT CENTER*/] =
                        samples[i * channels + 4 /*FRONT CENTER*/];
                    samplesCorrect[i * channels + 3 /*sub/lfe*/] =
                        samples[i * channels + 5 /*sub/lfe*/];
                    samplesCorrect[i * channels + 4 /*REAR LEFT*/] =
                        samples[i * channels + 2 /*REAR LEFT*/];
                    samplesCorrect[i * channels + 5 /*REAR RIGHT*/] =
                        samples[i * channels + 3 /*REAR RIGHT*/];

                    // Not sure about these two
                    samplesCorrect[i * channels + 6 /*SIDE LEFT*/] =
                        samples[i * channels + 6 /*SIDE LEFT*/];
                    samplesCorrect[i * channels + 7 /*SIDE RIGHT*/] =
                        samples[i * channels + 7 /*SIDE RIGHT*/];
                }

                Buffer.BlockCopy(samplesCorrect, 0, byteSamples, 0, e.SamplesPerBuffer * channels * sizeof(float));
            }
            else
            {
                Buffer.BlockCopy(samples, 0, byteSamples, 0, e.SamplesPerBuffer * channels * sizeof(float));
            }

            buffer.AddSamples(byteSamples, 0, e.SamplesPerBuffer * channels * sizeof(float));
            //Trace.WriteLine(buffer.BufferedDuration);
        }
    }
}
