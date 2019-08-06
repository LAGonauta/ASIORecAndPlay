using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using JM.LinqFaster;
using JM.LinqFaster.SIMD;
using System.Runtime.InteropServices;

namespace ASIORecAndPlay
{
  internal class RecAndPlay : IDisposable
  {
    private const int maxChannels = 8;
    private const int intSize = sizeof(Int32);
    private const int maxSamples = 1 << 14;

    private AsioOut asioRec;
    private AsioOut asioPlay;
    private BufferedWaveProvider playBackBuffer;

    private int[] deinterleavedMappedOutputSamples = new int[maxSamples * maxChannels];
    private int[] interleavedOutputSamples = new int[maxSamples * maxChannels];
    private byte[] interleavedBytesOutputSamples = new byte[maxSamples * intSize * maxChannels];

    public bool Valid { get; private set; }
    public bool CalculateRMS { get; set; }

    private IEnumerable<(uint inputChannel, uint outputChannel)> channelMapping;
    private uint firstInputChannel, lastInputChannel, firstOutputChannel, lastOutputChannel;

    public RecAndPlay(string recordingDevice, string playingDevice, ChannelMapping channelMapping)
    {
      asioRec = new AsioOut(recordingDevice);
      asioPlay = new AsioOut(playingDevice);

      if (channelMapping.OutputChannels.Any() == false || !ValidChannelMapping(channelMapping))
      {
        return;
      }

      this.channelMapping = channelMapping.GetMappingList();

      firstInputChannel = channelMapping.InputChannels.Min();
      lastInputChannel = channelMapping.InputChannels.Max();

      asioRec.InputChannelOffset = (int)firstInputChannel;
      asioRec.InitRecordAndPlayback(null, (int)(lastInputChannel - firstInputChannel) + 1, 48000);
      asioRec.AudioAvailable += new EventHandler<AsioAudioAvailableEventArgs>(OnAudioAvailable);

      firstOutputChannel = channelMapping.OutputChannels.Min();
      lastOutputChannel = channelMapping.OutputChannels.Max();

      // Still needs to think about the logic to map each channel to the volume meter
      firstOutputChannel = 0;
      lastOutputChannel = 7;

      //var format = new WaveFormat(48000, 32, (int)(lastOutputChannel - firstOutputChannel) + 1);
      var format = new WaveFormat(48000, 32, asioPlay.DriverOutputChannelCount);
      playBackBuffer = new BufferedWaveProvider(format);

      asioPlay.ChannelOffset = (int)firstOutputChannel;
      asioPlay.Init(playBackBuffer);
      Valid = true;
    }

    private bool ValidChannelMapping(ChannelMapping channelMapping)
    {
      if (channelMapping.InputChannels.Max() > asioRec.DriverInputChannelCount ||
          channelMapping.OutputChannels.Max() > asioPlay.DriverOutputChannelCount)
      {
        return false;
      }

      return true;
    }

    #region Recording

    public string[] GetRecordingDeviceChannelsNames()
    {
      if (!Valid)
      {
        return new string[0];
      }

      List<string> names = new List<string>();
      for (int i = 0; i < asioRec.DriverInputChannelCount; ++i)
      {
        names.Add(asioRec.AsioInputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowRecordingControlPanel()
    {
      if (!Valid)
      {
        return;
      }

      asioRec.ShowControlPanel();
    }

    #endregion Recording

    #region Playback

    public string[] GetPlaybackDeviceChannelsNames()
    {
      if (!Valid)
      {
        return new string[0];
      }

      List<string> names = new List<string>();
      for (int i = 0; i < asioPlay.DriverOutputChannelCount; ++i)
      {
        names.Add(asioPlay.AsioOutputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowPlaybackControlPanel()
    {
      if (!Valid)
      {
        return;
      }

      asioPlay.ShowControlPanel();
    }

    #endregion Playback

    public void Play()
    {
      if (!Valid)
      {
        return;
      }

      asioRec.Play();
      asioPlay.Play();
    }

    public void Stop()
    {
      if (!Valid)
      {
        return;
      }

      asioPlay.Stop();
      asioRec.Stop();
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

      if (asioRec.DriverName == device)
      {
        asioRec.ShowControlPanel();
      }

      if (asioPlay.DriverName == device)
      {
        asioPlay.ShowControlPanel();
      }
    }

    public void Dispose()
    {
      Valid = false;
      asioPlay.Dispose();
      asioRec.Dispose();
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

      Array.Clear(deinterleavedMappedOutputSamples, 0, e.SamplesPerBuffer * asioPlay.NumberOfOutputChannels);

      var mappings = channelMapping;
      if (mappings.Count() > 0)
      {
        foreach (var map in mappings.Select(item => (inputChannel: item.inputChannel - firstInputChannel, outputChannel: item.outputChannel - firstOutputChannel)))
        {
          Marshal.Copy(e.InputBuffers[map.inputChannel], deinterleavedMappedOutputSamples, (int)map.outputChannel * e.SamplesPerBuffer, e.SamplesPerBuffer);
        }

        for (int channelNumber = 0, totalChannels = asioPlay.NumberOfOutputChannels; channelNumber < totalChannels; ++channelNumber)
        {
          for (int sampleNumber = 0; sampleNumber < e.SamplesPerBuffer; ++sampleNumber)
          {
            interleavedOutputSamples[sampleNumber * totalChannels + channelNumber] =
              deinterleavedMappedOutputSamples[channelNumber * e.SamplesPerBuffer + sampleNumber];
          }
        }

        {
          var totalChannels = asioPlay.NumberOfOutputChannels;

          playbackAudioValue.Left.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 0);
          playbackAudioValue.Right.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 1);

          switch (totalChannels)
          {
            case 4:
              playbackAudioValue.BackLeft.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.BackRight.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 3);
              break;

            case 6:
              playbackAudioValue.Center.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.Sub.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 3);
              playbackAudioValue.SideLeft.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 4);
              playbackAudioValue.SideRight.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 5);
              break;

            case 8:
              playbackAudioValue.Center.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 2);
              playbackAudioValue.Sub.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 3);
              playbackAudioValue.BackLeft.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 4);
              playbackAudioValue.BackRight.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 5);
              playbackAudioValue.SideLeft.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 6);
              playbackAudioValue.SideRight.Volume = (float)GetRMSVolume(e.SamplesPerBuffer, 7);
              break;
          }
        }

        Buffer.BlockCopy(interleavedOutputSamples, 0, interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * asioPlay.NumberOfOutputChannels * intSize);
      }

      playBackBuffer.AddSamples(interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * asioPlay.NumberOfOutputChannels * intSize);
    }

    private double GetRMSVolume(int samplesPerChannel, int channelNumber)
    {
      if (CalculateRMS)
      {
        return Math.Sqrt(deinterleavedMappedOutputSamples
                            .Slice(channelNumber * samplesPerChannel, samplesPerChannel)
                            .SelectF(item => (double)item * item / int.MaxValue / int.MaxValue)
                            .SumS() / samplesPerChannel);
      }
      return 0.0;
    }

    #endregion Private
  }
}