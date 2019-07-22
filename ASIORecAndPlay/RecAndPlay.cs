using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ASIORecAndPlay
{
  internal class RecAndPlay : IDisposable
  {
    private const int maxChannels = 8;
    private const int intSize = sizeof(Int32);
    private const int maxSamples = 1 << 16;

    private AsioOut asio_rec;
    private AsioOut asio_play;
    private BufferedWaveProvider playBackBuffer;

    private int[] singleChannelSamples = new int[maxSamples];
    private int[] deinterleavedMappedOutputSamples = new int[maxSamples * maxChannels];
    private int[] interleavedOutputSamples = new int[maxSamples * maxChannels];
    private byte[] interleavedBytesOutputSamples = new byte[maxSamples * intSize * maxChannels];

    private int playbackChannels = 2;
    private int recordingChannels = 2;
    public bool Valid { get; private set; }

    private List<(uint outputChannel, uint inputChannel)> channelMapping = new List<(uint outputChannel, uint inputChannel)>();

    /// <summary>
    /// Sets the desired output-input mapping. While it is possible to send one input to more than one output, an output can receive data from only one input.
    /// </summary>
    /// <param name="outputInputPairs">Key: output channel number, Value: input channel number</param>
    /// <returns>True if successful, False otherwise.</returns>
    public bool SetChannelMapping(IDictionary<uint, uint> outputInputPairs)
    {
      if (!Valid ||
        outputInputPairs.Values.Max() > asio_rec.NumberOfInputChannels ||
        outputInputPairs.Keys.Max() > asio_play.NumberOfOutputChannels)
      {
        return false;
      }

      channelMapping = outputInputPairs
        .Select(e => (outputChannel: e.Key, inputChannel: e.Value))
        .OrderBy(e => e.inputChannel).ToList();
      return true;
    }

    public RecAndPlay(string recordingDevice, int recordingChannels, string playingDevice, int playbackChannels)
    {
      this.recordingChannels = recordingChannels;
      this.playbackChannels = playbackChannels;

      asio_rec = new AsioOut(recordingDevice);

      asio_rec.InitRecordAndPlayback(null, recordingChannels, 48000);
      asio_rec.AudioAvailable += new EventHandler<AsioAudioAvailableEventArgs>(OnAudioAvailable);

      var format = new WaveFormat(48000, 32, playbackChannels);
      playBackBuffer = new BufferedWaveProvider(format);

      asio_play = new AsioOut(playingDevice);
      asio_play.Init(playBackBuffer);
      Valid = true;
    }

    #region Recording

    public string[] GetRecordingDeviceChannelsNames()
    {
      List<string> names = new List<string>();
      for (int i = 0; i < asio_rec?.DriverInputChannelCount; ++i)
      {
        names.Add(asio_rec.AsioInputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowRecordingControlPanel()
    {
      asio_rec?.ShowControlPanel();
    }

    #endregion Recording

    #region Playback

    public string[] GetPlaybackDeviceChannelsNames()
    {
      List<string> names = new List<string>();
      for (int i = 0; i < asio_play?.DriverOutputChannelCount; ++i)
      {
        names.Add(asio_play.AsioOutputChannelName(i));
      }
      return names.ToArray();
    }

    public void ShowPlaybackControlPanel()
    {
      asio_play?.ShowControlPanel();
    }

    #endregion Playback

    public void Play()
    {
      asio_rec.Play();
      asio_play.Play();
    }

    public void Stop()
    {
      asio_play.Stop();
      asio_rec.Stop();
    }

    public TimeSpan BufferedDuration()
    {
      return playBackBuffer.BufferedDuration;
    }

    public void ShowControlPanel(string device)
    {
      if (asio_rec?.DriverName == device)
      {
        asio_rec.ShowControlPanel();
      }

      if (asio_play?.DriverName == device)
      {
        asio_play.ShowControlPanel();
      }
    }

    private void OnAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
    {
      Array.Clear(deinterleavedMappedOutputSamples, 0, e.SamplesPerBuffer * asio_play.NumberOfOutputChannels);

      var mappings = channelMapping;
      if (mappings.Count > 0)
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

        for (int channelNumber = 0, totalChannels = asio_play.NumberOfOutputChannels; channelNumber < totalChannels; ++channelNumber)
        {
          for (int sample = 0; sample < e.SamplesPerBuffer; ++sample)
          {
            interleavedOutputSamples[sample * totalChannels + channelNumber] =
              deinterleavedMappedOutputSamples[channelNumber * e.SamplesPerBuffer + sample];
          }
        }

        Buffer.BlockCopy(interleavedOutputSamples, 0, interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * asio_play.NumberOfOutputChannels * intSize);
      }

      playBackBuffer.AddSamples(interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * asio_play.NumberOfOutputChannels * intSize);
    }

    public void Dispose()
    {
      Valid = false;
      asio_play.Dispose();
      asio_rec.Dispose();
    }
  }
}