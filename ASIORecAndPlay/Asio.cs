using NAudio.Wave;

namespace ASIORecAndPlay
{
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
  }
}