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
    private const int maxSamples = 1 << 16;

    private AsioOut asioRec;
    private AsioOut asioPlay;
    private BufferedWaveProvider playBackBuffer;

    private int[] singleChannelSamples = new int[maxSamples];
    private int[] deinterleavedMappedOutputSamples = new int[maxSamples * maxChannels];
    private int[] interleavedOutputSamples = new int[maxSamples * maxChannels];
    private byte[] interleavedBytesOutputSamples = new byte[maxSamples * intSize * maxChannels];

    private int playbackChannels = 2;
    private int recordingChannels = 2;
    public bool Valid { get; private set; }
    public bool CalculateRMS { get; set; }

    private IEnumerable<(uint inputChannel, uint outputChannel)> channelMapping;

    public RecAndPlay(string recordingDevice, int recordingChannels, string playingDevice, int playbackChannels, ChannelMapping channelMapping = null)
    {
      this.recordingChannels = recordingChannels;
      this.playbackChannels = playbackChannels;

      asioRec = new AsioOut(recordingDevice);

      asioRec.InitRecordAndPlayback(null, recordingChannels, 48000);
      asioRec.AudioAvailable += new EventHandler<AsioAudioAvailableEventArgs>(OnAudioAvailable);

      var format = new WaveFormat(48000, 32, playbackChannels);
      playBackBuffer = new BufferedWaveProvider(format);

      asioPlay = new AsioOut(playingDevice);
      asioPlay.Init(playBackBuffer);
      Valid = true;
      this.channelMapping = channelMapping?.GetMappingList() ?? new List<(uint, uint)>();
    }

    public bool SetChannelMapping(ChannelMapping channelMapping)
    {
      if (!Valid ||
        channelMapping.InputChannels.Max() > asioRec.NumberOfInputChannels ||
        channelMapping.OutputChannels.Max() > asioPlay.NumberOfOutputChannels)
      {
        return false;
      }

      this.channelMapping = channelMapping.GetMappingList();
      return true;
    }

    #region Recording

    public string[] GetRecordingDeviceChannelsNames()
    {
      List<string> names = new List<string>();
      for (int i = 0; i < asioRec?.DriverInputChannelCount; ++i)
      {
        names.Add(asioRec.AsioInputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowRecordingControlPanel()
    {
      asioRec?.ShowControlPanel();
    }

    #endregion Recording

    #region Playback

    public string[] GetPlaybackDeviceChannelsNames()
    {
      List<string> names = new List<string>();
      for (int i = 0; i < asioPlay?.DriverOutputChannelCount; ++i)
      {
        names.Add(asioPlay.AsioOutputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowPlaybackControlPanel()
    {
      asioPlay?.ShowControlPanel();
    }

    #endregion Playback

    public void Play()
    {
      asioRec.Play();
      asioPlay.Play();
    }

    public void Stop()
    {
      asioPlay.Stop();
      asioRec.Stop();
    }

    public TimeSpan BufferedDuration()
    {
      return playBackBuffer.BufferedDuration;
    }

    public void ShowControlPanel(string device)
    {
      if (asioRec?.DriverName == device)
      {
        asioRec.ShowControlPanel();
      }

      if (asioPlay?.DriverName == device)
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
      Array.Clear(deinterleavedMappedOutputSamples, 0, e.SamplesPerBuffer * asioPlay.NumberOfOutputChannels);

      var mappings = channelMapping;
      if (mappings.Count() > 0)
      {
        var lastInputChannel = uint.MaxValue;
        foreach (var map in mappings)
        {
          if (lastInputChannel != map.inputChannel)
          {
            Marshal.Copy(e.InputBuffers[map.inputChannel], singleChannelSamples, 0, e.SamplesPerBuffer);
            lastInputChannel = map.inputChannel;
          }
          Array.Copy(singleChannelSamples, 0, deinterleavedMappedOutputSamples, map.outputChannel * e.SamplesPerBuffer, e.SamplesPerBuffer);
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