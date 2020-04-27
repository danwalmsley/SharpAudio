using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;

namespace SharpAudio.FFMPEG
{
    public sealed unsafe class AudioContext : StreamContext
    {
        private AVFrame* _resampled;
        private SwrContext* _resampler;
        private AudioFormat _resampleTarget;
        private CircularBuffer _circBuf;
        private byte[] _tmpBuf;

        public CircularBuffer CircularBuffer => _circBuf;
        public int BufferCapacity { get; }

        /// <summary>
        /// Called once for each audio stream
        /// </summary>
        internal AudioContext(AVStream* stream, FFMPEGDecoder source, AudioFormat resampleTarget) : base(stream, source)
        {
            _resampleTarget = resampleTarget;
            BufferCapacity = resampleTarget.SampleRate * resampleTarget.Channels * 4;
            _circBuf = new CircularBuffer(BufferCapacity);
            CreateAudio();
        }

        /// <summary>
        /// Create the audio buffers in the handler. Check if resampling is required
        /// </summary>
        private void CreateAudio()
        {
            SetupResampler();
        }

        /// <summary>
        /// Called when a new packet arrives
        /// </summary>
        protected override void Update()
        {
            ffmpeg.swr_convert_frame(_resampler, _resampled, _decoded);

            //get the size of our data buffer
            int data_size = ffmpeg.av_samples_get_buffer_size(null, _codecCtx->channels, _resampled->nb_samples, (AVSampleFormat)_resampled->format, 1);

            if (_tmpBuf is null || _tmpBuf?.Length < data_size)
            {
                _tmpBuf = new byte[data_size];
            }

            fixed (byte* tmp = &_tmpBuf[0])
                Buffer.MemoryCopy(_decoded->data[0], tmp, data_size, data_size);

            _circBuf.Write(_tmpBuf, 0, data_size);
        }


        /// <summary>
        /// create a resampler to convert the audio format to our output format
        /// </summary>
        private void SetupResampler()
        {
            _resampled = ffmpeg.av_frame_alloc();
            _resampled->channel_layout = (ulong)ffmpeg.av_get_default_channel_layout(_source._DESIRED_CHANNELS);
            _resampled->sample_rate = _source._DESIRED_SAMPLE_RATE;
            _resampled->format = (int)_source._DESIRED_SAMPLE_FORMAT;

            // Fixes SWR @ 0x2192200] Input channel count and layout are unset error.
            if (_codecCtx->channel_layout == 0)
            {
                _codecCtx->channel_layout = (ulong)ffmpeg.av_get_default_channel_layout(_codecCtx->channels);
            }

            //we only want to change from planar to interleaved
            _resampler = ffmpeg.swr_alloc_set_opts(null,
                (long)_resampled->channel_layout,      //Out layout should be identical to input layout
                (AVSampleFormat)_resampled->format,
                _resampled->sample_rate,    //Out frequency should be identical to input frequency
                (long)_codecCtx->channel_layout,
                _codecCtx->sample_fmt,
                _codecCtx->sample_rate,
                0, null);       //No logging

            if (ffmpeg.swr_init(_resampler) != 0)
            {
                throw new InvalidOperationException("Can't init resampler!");
            }
        }
    }
}
