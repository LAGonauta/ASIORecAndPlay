using NAudio.CoreAudioApi;
using System;
using System.Linq;

namespace ASIORecAndPlay
{
  public enum ChannelLayout
  {
    Mono,
    Stereo,
    Quad,
    S51,
    S71
  }

  public static class Wasapi
  {
    public static MMDevice[] Endpoints(DataFlow dataFlow, DeviceState dwStateMask)
    {
      var enumerator = new MMDeviceEnumerator();
      return enumerator.EnumerateAudioEndPoints(dataFlow, dwStateMask).ToArray();
    }

    public static string[] GetChannelNames(ChannelLayout channelLayout)
    {
      switch (channelLayout)
      {
        case ChannelLayout.Mono:
          return new string[] { "Center " };

        case ChannelLayout.Stereo:
          return new string[] { "Left", "Right" };

        case ChannelLayout.Quad:
          return new string[] { "Left", "Right", "Back Left", "Back Right" };

        case ChannelLayout.S51:
          return new string[] { "Left", "Right", "Center", "Sub", "Side Left", "Side Right" };

        case ChannelLayout.S71:
          return new string[] { "Left", "Right", "Center", "Sub", "Back Left", "Back Right", "Side Left", "Side Right" };

        default:
          throw new NotImplementedException();
      }
    }

    public static int NumChannels(this ChannelLayout channelLayout)
    {
      switch (channelLayout)
      {
        case ChannelLayout.Mono:
          return 1;

        case ChannelLayout.Stereo:
          return 2;

        case ChannelLayout.Quad:
          return 4;

        case ChannelLayout.S51:
          return 6;

        case ChannelLayout.S71:
          return 8;

        default:
          throw new NotImplementedException();
      }
    }
  }
}