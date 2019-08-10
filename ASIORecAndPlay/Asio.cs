using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace ASIORecAndPlay
{
  internal enum ChannelType
  {
    Input,
    Output
  }

  internal static class Asio
  {
    public static string[] GetDevices()
    {
      return AsioOut.GetDriverNames();
    }

    public static void ShowControlPanel(string device)
    {
      using (var asio = new AsioOut(device))
      {
        asio.ShowControlPanel();
      }
    }

    public static string[] GetChannelNames(string device, ChannelType channelType)
    {
      using (var asio = new AsioOut(device))
      {
        int count = asio.DriverOutputChannelCount;
        Func<int, string> getName = (i) => asio.AsioOutputChannelName(i);
        if (channelType == ChannelType.Input)
        {
          count = asio.DriverInputChannelCount;
          getName = (i) => asio.AsioInputChannelName(i);
        }

        var nameList = new List<string>();
        for (int i = 0; i < count; ++i)
        {
          nameList.Add(getName(i));
        }
        return nameList.ToArray();
      }
    }
  }
}