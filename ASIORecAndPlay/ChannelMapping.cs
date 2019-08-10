using System.Collections.Generic;
using System.Linq;

namespace ASIORecAndPlay
{
  internal class ChannelMapping
  {
    private IDictionary<int, int> channelMapping;
    private object _lock = new object();

    public IEnumerable<int> InputChannels => channelMapping.Values;

    public IEnumerable<int> OutputChannels => channelMapping.Keys;

    public ChannelMapping()
    {
      channelMapping = new Dictionary<int, int>();
    }

    public ChannelMapping(IDictionary<int, int> outputInputPairs)
    {
      channelMapping = outputInputPairs;
    }

    /// <summary>
    /// While one input can send to various outputs, one output can receive only from one input.
    /// Thus, the output must be unique.
    /// </summary>
    /// <param name="inputChannel">Input channel number</param>
    /// <param name="outputChannel">Output channel number</param>
    /// <returns></returns>
    public bool Add(int inputChannel, int outputChannel)
    {
      try
      {
        lock (_lock)
        {
          channelMapping.Add(outputChannel, inputChannel);
        }

        return true;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Remove the mapping to the set output channel.
    /// </summary>
    /// <param name="outputChannel"></param>
    /// <returns></returns>
    public bool Remove(int outputChannel)
    {
      lock (_lock)
      {
        return channelMapping.Remove(outputChannel);
      }
    }

    /// <summary>
    /// Gets an ordered enumerable of a copy the mapping, ordered by input channel.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<(int inputChannel, int outputChannel)> GetMappingList()
    {
      lock (_lock)
      {
        return channelMapping
        .Select(e => (inputChannel: e.Value, outputChannel: e.Key))
        .OrderBy(e => e.inputChannel).ToList();
      }
    }

    /// <summary>
    /// Gets a copy of the internal dictionary.
    /// </summary>
    /// <returns></returns>
    public IDictionary<int, int> GetMappingDictionary()
    {
      lock (_lock)
      {
        return channelMapping.ToDictionary(e => e.Key, e => e.Value);
      }
    }
  }
}