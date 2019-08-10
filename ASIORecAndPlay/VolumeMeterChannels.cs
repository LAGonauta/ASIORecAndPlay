using AudioVUMeter;

namespace ASIORecAndPlay
{
  public struct VolumeMeterChannels
  {
    internal VUValue Left;
    internal VUValue Right;

    internal VUValue Center;
    internal VUValue BackLeft;
    internal VUValue BackRight;

    internal VUValue SideLeft;
    internal VUValue SideRight;

    internal VUValue Sub;
  }
}