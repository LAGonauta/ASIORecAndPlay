using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ASIORecAndPlay
{
  internal class RecAndPlay : IDisposable
  {
    private const int maxChannels = 8;
    private const int intSize = sizeof(Int32);
    private const int maxSamples = 1 << 14;

    private AsioOut recDevice;
    private IWavePlayer playDevice;

    private BufferedWaveProvider playBackBuffer;

    private int[] interleavedOutputSamples = new int[maxSamples * maxChannels];
    private byte[] interleavedBytesOutputSamples = new byte[maxSamples * intSize * maxChannels];

    public bool Valid { get; private set; }
    public bool CalculateRMS { get; set; }

    private IEnumerable<Mapping> channelMapping;
    private int firstInputChannel, lastInputChannel, numOutputChannels;

    public RecAndPlay(AsioOut recordingDevice, IWavePlayer playingDevice, ChannelMapping channelMapping, ChannelLayout? forcedNumOutputChannels = null)
    {
      recDevice = recordingDevice;
      playDevice = playingDevice;

      if (channelMapping.OutputChannels.Any() == false || !ValidChannelMapping(channelMapping))
      {
        return;
      }

      this.channelMapping = channelMapping.GetMappingList();

      firstInputChannel = channelMapping.InputChannels.Min();
      lastInputChannel = channelMapping.InputChannels.Max();

      recDevice.InputChannelOffset = firstInputChannel;
      recDevice.InitRecordAndPlayback(null, (lastInputChannel - firstInputChannel) + 1, 48000);
      recDevice.AudioAvailable += new EventHandler<AsioAudioAvailableEventArgs>(OnAudioAvailable);

      numOutputChannels = forcedNumOutputChannels?.NumChannels() ?? 8;
      var play = playDevice as AsioOut;
      if (play != null)
      {
        numOutputChannels = play.DriverOutputChannelCount;
      }
      var format = new WaveFormat(48000, 32, numOutputChannels);
      playBackBuffer = new BufferedWaveProvider(format);
      playDevice.Init(playBackBuffer);
      Valid = true;
    }

    private bool ValidChannelMapping(ChannelMapping channelMapping)
    {
      if (channelMapping.InputChannels.Max() > recDevice.DriverInputChannelCount)// ||
                                                                                 //channelMapping.OutputChannels.Max() > asioPlay.DriverOutputChannelCount)
      {
        return false;
      }

      return true;
    }

    public void Play()
    {
      if (!Valid)
      {
        return;
      }

      recDevice.Play();
      playDevice.Play();
      Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
    }

    public void Stop()
    {
      if (!Valid)
      {
        return;
      }

      playDevice.Stop();
      recDevice.Stop();
      Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
    }

    public TimeSpan BufferedDuration()
    {
      return playBackBuffer?.BufferedDuration ?? TimeSpan.Zero;
    }

    public void ShowControlPanel(string device)
    {
      if (!Valid)
      {
        return;
      }

      if (recDevice.DriverName == device)
      {
        recDevice.ShowControlPanel();
      }

      var asioPlay = playDevice as AsioOut;
      if (asioPlay != null)
      {
        if (asioPlay.DriverName == device)
        {
          asioPlay.ShowControlPanel();
        }
      }
    }

    public void Dispose()
    {
      Valid = false;
      playDevice.Dispose();
      recDevice.Dispose();
    }

    private VolumeMeterChannels playbackAudioValue = new VolumeMeterChannels();
    public VolumeMeterChannels PlaybackAudioValue => playbackAudioValue;

    #region Private

    private void OnAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
    {
      if (!Valid)
      {
        return;
      }

      Array.Clear(interleavedOutputSamples, 0, e.SamplesPerBuffer * numOutputChannels);
      var mappings = channelMapping;
      if (mappings.Count() > 0)
      {
        foreach (var map in mappings.Select(item => new Mapping { inputChannel = item.inputChannel - firstInputChannel, outputChannel = item.outputChannel }))
        {
          for (int sampleNumber = 0; sampleNumber < e.SamplesPerBuffer; ++sampleNumber)
          {
            interleavedOutputSamples[sampleNumber * numOutputChannels + map.outputChannel] = GetInputSampleInt32LSB(e.InputBuffers[map.inputChannel], sampleNumber);
          }
        }

        if (CalculateRMS)
        {
          switch (numOutputChannels)
          {
            case 1:
              playbackAudioValue.Center.Volume = GetRMSVolume(e.SamplesPerBuffer, 0);
              break;

            case 2:
              playbackAudioValue.Left.Volume = GetRMSVolume(e.SamplesPerBuffer, 0);
              playbackAudioValue.Right.Volume = GetRMSVolume(e.SamplesPerBuffer, 1);
              break;

            case 4:
              playbackAudioValue.Left.Volume = GetRMSVolume(e.SamplesPerBuffer, 0);
              playbackAudioValue.Right.Volume = GetRMSVolume(e.SamplesPerBuffer, 1);
              playbackAudioValue.BackLeft.Volume = GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.BackRight.Volume = GetRMSVolume(e.SamplesPerBuffer, 3);
              break;

            case 6:
              playbackAudioValue.Left.Volume = GetRMSVolume(e.SamplesPerBuffer, 0);
              playbackAudioValue.Right.Volume = GetRMSVolume(e.SamplesPerBuffer, 1);
              playbackAudioValue.Center.Volume = GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.Sub.Volume = GetRMSVolume(e.SamplesPerBuffer, 3);
              playbackAudioValue.SideLeft.Volume = GetRMSVolume(e.SamplesPerBuffer, 4);
              playbackAudioValue.SideRight.Volume = GetRMSVolume(e.SamplesPerBuffer, 5);
              break;

            case 8:
              playbackAudioValue.Left.Volume = GetRMSVolume(e.SamplesPerBuffer, 0);
              playbackAudioValue.Right.Volume = GetRMSVolume(e.SamplesPerBuffer, 1);
              playbackAudioValue.Center.Volume = GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.Sub.Volume = GetRMSVolume(e.SamplesPerBuffer, 3);
              playbackAudioValue.BackLeft.Volume = GetRMSVolume(e.SamplesPerBuffer, 4);
              playbackAudioValue.BackRight.Volume = GetRMSVolume(e.SamplesPerBuffer, 5);
              playbackAudioValue.SideLeft.Volume = GetRMSVolume(e.SamplesPerBuffer, 6);
              playbackAudioValue.SideRight.Volume = GetRMSVolume(e.SamplesPerBuffer, 7);
              break;
          }
        }

        Buffer.BlockCopy(interleavedOutputSamples, 0, interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * numOutputChannels * intSize);
      }

      playBackBuffer.AddSamples(interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * numOutputChannels * intSize);
    }

    private float GetRMSVolume(int samplesPerChannel, int channelNumber)
    {
      var sum = 0.0f;
      for (int i = 0; i < samplesPerChannel; ++i)
      {
        float item = interleavedOutputSamples[channelNumber + i * numOutputChannels];
        sum += item * item / ((float)int.MaxValue * int.MaxValue);
      }

      return (float)Math.Sqrt(sum / samplesPerChannel);
    }

    private unsafe int GetInputSampleInt32LSB(IntPtr inputBuffer, int n)
    {
      return *((int*)inputBuffer + n);
    }

    #endregion Private
  }
}