using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ASIORecAndPlay
{
    internal class RecAndPlay : IDisposable
    {
        private const int _maxChannels = 8;
        private const int _intSize = sizeof(Int32);
        private const int _maxSamples = 1 << 14;

        private AsioOut _recDevice;
        private IWavePlayer _playDevice;

        private BufferedWaveProvider _playBackBuffer;

        private int[] _interleavedOutputSamples = new int[_maxSamples * _maxChannels];
        private byte[] _interleavedBytesOutputSamples = new byte[_maxSamples * _intSize * _maxChannels];

        public bool Valid { get; private set; }

        public bool CalculateRMS { get; set; }
        private VolumeMeterChannels _playbackAudioValue = new VolumeMeterChannels();
        public VolumeMeterChannels PlaybackAudioValue => _playbackAudioValue;

        private List<Mapping> _channelMapping;
        private int _numOutputChannels;

        public RecAndPlay(AsioOut recordingDevice, IWavePlayer playingDevice, ChannelMapping channelMapping, ChannelLayout? forcedNumOutputChannels = null, Action onDeviceError = null)
        {
            var frequency = 48000;
            _recDevice = recordingDevice;
            _playDevice = playingDevice;

            if (channelMapping.OutputChannels.Any() == false || !ValidChannelMapping(channelMapping))
            {
                return;
            }

            var mappings = channelMapping.GetMappingList();

            var firstInputChannel = channelMapping.InputChannels.Min();
            var lastInputChannel = channelMapping.InputChannels.Max();

            _channelMapping = mappings
                .Select(item => new Mapping { inputChannel = item.inputChannel - firstInputChannel, outputChannel = item.outputChannel })
                .ToList();

            _recDevice.InputChannelOffset = firstInputChannel;
            _recDevice.InitRecordAndPlayback(null, (lastInputChannel - firstInputChannel) + 1, frequency);
            _recDevice.AudioAvailable += new EventHandler<AsioAudioAvailableEventArgs>(OnAudioAvailable);

            _numOutputChannels = forcedNumOutputChannels?.NumChannels() ?? 8;
            if (_playDevice is AsioOut asioOut)
            {
                _numOutputChannels = asioOut.DriverOutputChannelCount;
            }
            var format = new WaveFormat(frequency, 32, _numOutputChannels);
            _playBackBuffer = new BufferedWaveProvider(format);
            _playBackBuffer.DiscardOnBufferOverflow = true;
            _playDevice.Init(_playBackBuffer);

            if (onDeviceError != null) {
                _playDevice.PlaybackStopped += new EventHandler<StoppedEventArgs>((_, _) =>
                {
                    if (!Valid)
                    {
                        return;
                    }

                    onDeviceError();
                });
            }

            Valid = true;
        }

        private bool ValidChannelMapping(ChannelMapping channelMapping)
        {
            if (channelMapping.InputChannels.Max() > _recDevice.DriverInputChannelCount)// ||
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

            _recDevice.Play();
            _playDevice.Play();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
        }

        public void Stop()
        {
            if (!Valid)
            {
                return;
            }

            _playDevice.Stop();
            _recDevice.Stop();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        }

        public TimeSpan BufferedDuration()
        {
            return _playBackBuffer?.BufferedDuration ?? TimeSpan.Zero;
        }

        public void ShowControlPanel(string device)
        {
            if (!Valid)
            {
                return;
            }

            if (_recDevice.DriverName == device)
            {
                _recDevice.ShowControlPanel();
            }

            var asioPlay = _playDevice as AsioOut;
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
            if (Valid)
            {
                Valid = false;
                _playDevice.Dispose();
                _recDevice.Dispose();
            }
        }

        #region Private

        private void OnAudioAvailable(object _, AsioAudioAvailableEventArgs e)
        {
            if (!Valid)
            {
                return;
            }

            if (_channelMapping.Any())
            {
                foreach (var map in _channelMapping)
                {
                    for (int sampleNumber = 0; sampleNumber < e.SamplesPerBuffer; ++sampleNumber)
                    {
                        _interleavedOutputSamples[sampleNumber * _numOutputChannels + map.outputChannel] = GetInputSampleInt32LSB(e.InputBuffers[map.inputChannel], sampleNumber);
                    }
                }

                if (CalculateRMS)
                {
                    _playbackAudioValue = GenerateNewVolumeMetersValues(e.SamplesPerBuffer, _numOutputChannels, _interleavedOutputSamples);
                }

                Buffer.BlockCopy(_interleavedOutputSamples, 0, _interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * _numOutputChannels * _intSize);
            }

            _playBackBuffer.AddSamples(_interleavedBytesOutputSamples, 0, e.SamplesPerBuffer * _numOutputChannels * _intSize);
        }

        private VolumeMeterChannels GenerateNewVolumeMetersValues(int samplesPerBuffer, int numOutputChannels, IList<int> samples)
        {
            var channels = new VolumeMeterChannels();
            switch (numOutputChannels)
            {
                case 1:
                    channels.Center.Volume = GetRMSVolume(samplesPerBuffer, 0, numOutputChannels, samples);
                    break;

                case 2:
                    channels.Left.Volume = GetRMSVolume(samplesPerBuffer, 0, numOutputChannels, samples);
                    channels.Right.Volume = GetRMSVolume(samplesPerBuffer, 1, numOutputChannels, samples);
                    break;

                case 4:
                    channels.Left.Volume = GetRMSVolume(samplesPerBuffer, 0, numOutputChannels, samples);
                    channels.Right.Volume = GetRMSVolume(samplesPerBuffer, 1, numOutputChannels, samples);
                    channels.BackLeft.Volume = GetRMSVolume(samplesPerBuffer, 2, numOutputChannels, samples);
                    channels.BackRight.Volume = GetRMSVolume(samplesPerBuffer, 3, numOutputChannels, samples);
                    break;

                case 6:
                    channels.Left.Volume = GetRMSVolume(samplesPerBuffer, 0, numOutputChannels, samples);
                    channels.Right.Volume = GetRMSVolume(samplesPerBuffer, 1, numOutputChannels, samples);
                    channels.Center.Volume = GetRMSVolume(samplesPerBuffer, 2, numOutputChannels, samples);
                    channels.Sub.Volume = GetRMSVolume(samplesPerBuffer, 3, numOutputChannels, samples);
                    channels.SideLeft.Volume = GetRMSVolume(samplesPerBuffer, 4, numOutputChannels, samples);
                    channels.SideRight.Volume = GetRMSVolume(samplesPerBuffer, 5, numOutputChannels, samples);
                    break;

                case 8:
                    channels.Left.Volume = GetRMSVolume(samplesPerBuffer, 0, numOutputChannels, samples);
                    channels.Right.Volume = GetRMSVolume(samplesPerBuffer, 1, numOutputChannels, samples);
                    channels.Center.Volume = GetRMSVolume(samplesPerBuffer, 2, numOutputChannels, samples);
                    channels.Sub.Volume = GetRMSVolume(samplesPerBuffer, 3, numOutputChannels, samples);
                    channels.BackLeft.Volume = GetRMSVolume(samplesPerBuffer, 4, numOutputChannels, samples);
                    channels.BackRight.Volume = GetRMSVolume(samplesPerBuffer, 5, numOutputChannels, samples);
                    channels.SideLeft.Volume = GetRMSVolume(samplesPerBuffer, 6, numOutputChannels, samples);
                    channels.SideRight.Volume = GetRMSVolume(samplesPerBuffer, 7, numOutputChannels, samples);
                    break;
            }
            return channels;
        }

        private float GetRMSVolume(int samplesPerChannel, int channelNumber, int numOutputChannels, IList<int> samples)
        {
            var sum = 0.0f;
            for (int i = 0; i < samplesPerChannel; ++i)
            {
                float item = samples[channelNumber + i * numOutputChannels];
                sum += (item * item) / ((float)int.MaxValue * int.MaxValue);
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