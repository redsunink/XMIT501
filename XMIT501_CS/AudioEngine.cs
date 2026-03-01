using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Concentus.Enums;
using Concentus.Structs;

namespace XMIT501_CS
{
    public class AudioEngine
    {
        private WaveInEvent _waveIn;
        private OpusEncoder _encoder;
        
        private WaveOutEvent _waveOut;
        private MixingSampleProvider _mixer;
        private OpusDecoder _decoder;
        private Dictionary<byte, BufferedWaveProvider> _rxBuffers = new Dictionary<byte, BufferedWaveProvider>();

        private const int SampleRate = 48000;
        private const int Channels = 1;
        private const int FrameSize = 960; 
        private const int FrameBytes = FrameSize * 2; 

        private byte[] _pcmBuffer = new byte[FrameBytes];
        private int _pcmBufferPosition = 0;

        // FIXED: Added '?' to handle nullability warning
        public event Action<byte[]>? OnEncodedPacketReady;
        public event Action<float>? OnVolumeProcessed;

        public AudioEngine()
        {
            // FIXED: Used standard constructors instead of .Create()
            _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = 24000;

            _decoder = new OpusDecoder(SampleRate, Channels);

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, 16, Channels),
                BufferMilliseconds = 20
            };
            _waveIn.DataAvailable += WaveIn_DataAvailable;

            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels));
            _mixer.ReadFully = true; 
            
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_mixer);
            _waveOut.Play();
        }

        public void StartTransmitting()
        {
            _waveOut.Volume = 0f;
            _pcmBufferPosition = 0;
            _waveIn.StartRecording();
        }

        public void StopTransmitting()
        {
            _waveIn.StopRecording();
            _waveOut.Volume = 1f;
        }

        // FIXED: Added object? to match NAudio's event delegate
        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded; i++)
            {
                _pcmBuffer[_pcmBufferPosition++] = e.Buffer[i];

                if (_pcmBufferPosition == FrameBytes)
                {
                    EncodeAndEmitFrame();
                    _pcmBufferPosition = 0;
                }
            }
        }

        private void EncodeAndEmitFrame()
        {
            short[] shortBuffer = new short[FrameSize];
            Buffer.BlockCopy(_pcmBuffer, 0, shortBuffer, 0, FrameBytes);

            byte[] encodedData = new byte[1000]; 
            
            // FIXED: Used .AsSpan() to clear the obsolete warning
            int encodedLength = _encoder.Encode(shortBuffer.AsSpan(), FrameSize, encodedData.AsSpan(), encodedData.Length);

            if (encodedLength > 0)
            {
                byte[] finalPacket = new byte[encodedLength];
                Buffer.BlockCopy(encodedData, 0, finalPacket, 0, encodedLength);
                
                OnEncodedPacketReady?.Invoke(finalPacket);
            }
        }

        public void ReceiveNetworkAudio(byte channelId, byte[] opusData)
        {
            // 1. If this is the first time we've heard this channel, create a speaker buffer for it
            if (!_rxBuffers.TryGetValue(channelId, out var buffer))
            {
                buffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels));
                buffer.DiscardOnBufferOverflow = true; // Prevents massive lag if network stutters
                
                _rxBuffers[channelId] = buffer;
                
                // ToSampleProvider() automatically converts 16-bit PCM to IEEE Float for the Mixer
                _mixer.AddMixerInput(buffer.ToSampleProvider());
            }

            // 2. Decode the Opus data back into audio samples (shorts)
            short[] decodedShorts = new short[FrameSize];
            int decodedLength = _decoder.Decode(opusData.AsSpan(), decodedShorts.AsSpan(), FrameSize);

            // 3. Convert shorts back to bytes for NAudio
            byte[] decodedBytes = new byte[decodedLength * 2];
            Buffer.BlockCopy(decodedShorts, 0, decodedBytes, 0, decodedBytes.Length);

            // 4. Push it to the speakers
            buffer.AddSamples(decodedBytes, 0, decodedBytes.Length);
            float max = 0;
            foreach (var sample in decodedShorts)
            {
                // Convert the short to a float between 0.0 and 1.0
                float floatSample = Math.Abs(sample) / 32768f; 
                if (floatSample > max) max = floatSample;
            }
            OnVolumeProcessed?.Invoke(max);
        }

        public void Shutdown()
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveOut?.Stop();
            _waveOut?.Dispose();
        }
    }
}