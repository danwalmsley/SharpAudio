using System.Threading;
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SharpAudio.FFMPEG
{
    public unsafe sealed class FFMPEGDecoder : Decoder
    {
        //FFMPEG structs
        private AvStream _avStream;
        private int _targetStreamIndex;
        private TimeSpan _duration;
        private AudioContext _stream;
        private long _bitRate;
        private PlayState _state;
        private AVFormatContext* _fmtCtx;
        private AVPacket* _avPacket;
        private bool _isFinished;

        public override bool IsFinished => _isFinished;

        public readonly AVSampleFormat _DESIRED_SAMPLE_FORMAT = AVSampleFormat.AV_SAMPLE_FMT_S16;
        public readonly int _DESIRED_SAMPLE_RATE = 44_100;
        public readonly int _DESIRED_CHANNELS = 2;

        public unsafe FFMPEGDecoder(Stream stream)
        {
            _state = PlayState.Stopped;

            //allocate a new format context
            _fmtCtx = ffmpeg.avformat_alloc_context();

            //create a custom avstream to read from C#'s Stream
            _avStream = new AvStream(stream);
            _avStream.Attach(_fmtCtx);

            fixed (AVFormatContext** ctxPtr = &_fmtCtx)
            {
                if (ffmpeg.avformat_open_input(ctxPtr, "", null, null) != 0)
                {
                    throw new InvalidDataException("Cannot open input");
                }
            }

            if (ffmpeg.avformat_find_stream_info(_fmtCtx, null) < 0)
            {
                throw new InvalidDataException("Cannot find stream info");
            }

            double ms = _fmtCtx->duration / (double)ffmpeg.AV_TIME_BASE;
            _duration = TimeSpan.FromSeconds(ms);
            _bitRate = _fmtCtx->bit_rate;

            _targetStreamIndex = 0;

            _audioFormat.SampleRate = _DESIRED_SAMPLE_RATE;
            _audioFormat.Channels = _DESIRED_CHANNELS;
            _audioFormat.BitsPerSample = 16;

            _numSamples = (int)(_duration.TotalSeconds * _DESIRED_SAMPLE_RATE * _DESIRED_CHANNELS);

            //Iterate over all streams to get the overall number
            for (int i = 0; i < _fmtCtx->nb_streams; i++)
            {
                var avStream = _fmtCtx->streams[i];
                switch (avStream->codec->codec_type)
                {
                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        break;
                    case AVMediaType.AVMEDIA_TYPE_AUDIO:
                        _targetStreamIndex = i;
                        _stream = new AudioContext(avStream, this, _audioFormat);
                        break;
                    default:
                        throw new NotSupportedException("Invalid stream type, which is not suppurted!");
                }
            }

            _avPacket = ffmpeg.av_packet_alloc();
        }

        public unsafe bool GetFrame()
        {
            bool keepGoing = false;

            while (ffmpeg.av_read_frame(_fmtCtx, _avPacket) >= 0)
            {
                if (_avPacket == null)
                {
                    throw new InvalidDataException("Empty packet! Probably should continue");
                }

                int streamIdx = _avPacket->stream_index;

                if (streamIdx != _targetStreamIndex)
                    continue;

                _stream.ReceivePacket(_avPacket);
            }

            return keepGoing;
        }

        public void Loop()
        {
            while (_state == PlayState.Playing)
            {
                if (_stream.CircularBuffer.Length < (_stream.BufferCapacity / 2))
                    if (!GetFrame())
                        {
                            _isFinished = true;
                        }
 
            }
        }

        public override void Start()
        {
            //should reset to frame 0 
            if (_state == PlayState.Stopped)
            {
                // Reset();
                _state = PlayState.Playing;
            }

            Task.Factory.StartNew(Loop, TaskCreationOptions.LongRunning);
        }

        //reset the codecCtx
        private unsafe void Reset()
        {
            //flush the format context
            if (ffmpeg.avformat_flush(_fmtCtx) < 0)
            {
                throw new InvalidOperationException();
            }

            //flush stream
            _stream.Reset();

        }

        public override long GetSamples(int samples, ref byte[] data)
        {
            data = new byte[samples];
            return _stream.CircularBuffer.Read(data, 0, samples);
        }

        public override void Pause()
        {
            throw new NotImplementedException();
        }

        ~FFMPEGDecoder()
        {
            unsafe
            {
                fixed (AVFormatContext** ctxPtr = &_fmtCtx)
                {
                    ffmpeg.avformat_close_input(ctxPtr);
                };

                fixed (AVPacket** pktPtr = &_avPacket)
                {
                    ffmpeg.av_packet_free(pktPtr);
                };
            }
        }
    }
}
