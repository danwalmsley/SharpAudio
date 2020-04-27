using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpAudio.FFMPEG
{
    internal unsafe sealed class AvStream
    {
        private Stream _stream;
        private AVIOContext* _context;
        private byte[] _buffer;
        private avio_alloc_context_read_packet read_l;
        private avio_alloc_context_seek seek_l;
        private const int _bufSize = 32 * 1024;

        private object readlock = new object();

        public AvStream(Stream stream)
        {
            _stream = stream;
            _buffer = new byte[_bufSize];
            this.read_l = new avio_alloc_context_read_packet(ReadPacket);
            this.seek_l = new avio_alloc_context_seek(SeekFunc);

            fixed (byte* bufPtr = _buffer)
            {
                //create an unwritable context
                _context = ffmpeg.avio_alloc_context(bufPtr,
                    _bufSize, 0, null,
                    read_l,
                    null,
                    seek_l
                   );
            }
        }

        public void Attach(AVFormatContext* ctx)
        {
            ctx->pb = _context;
            ctx->flags = ffmpeg.AVFMT_FLAG_CUSTOM_IO;

            int size = _bufSize - ffmpeg.AVPROBE_PADDING_SIZE;
            int readBytes = _stream.Read(_buffer, 0, size);
            _stream.Seek(0, SeekOrigin.Begin);

            AVProbeData probe;
            fixed (byte* bufPtr = _buffer)
            {
                probe.buf = bufPtr;
                probe.buf_size = readBytes;
                probe.filename = null;
                probe.mime_type = null;
            }

            // Determine the input-format:
            ctx->iformat = ffmpeg.av_probe_input_format(&probe, 1);
        }

        private int ReadPacket(void* opaque, byte* buffer, int bufferSize)
        {
            lock (readlock)
                try
                {
                    var readCount = _stream.Read(_buffer, 0, _buffer.Length);
                    if (readCount > 0)
                        Marshal.Copy(_buffer, 0, (IntPtr)buffer, readCount);

                    return readCount;
                }
                catch (Exception)
                {
                    return ffmpeg.AVERROR_EOF;
                }
        }

        private long SeekFunc(void* opaque, long offset, int whence)
        {

            lock (readlock)
            {
                SeekOrigin origin;

                switch (whence)
                {
                    case ffmpeg.AVSEEK_SIZE:
                        return _stream.Length;
                    case 0:
                    case 1:
                    case 2:
                        origin = (SeekOrigin)whence;
                        break;
                    default:
                        throw new InvalidOperationException("Invalid whence");
                }

                _stream.Seek(offset, origin);
                return _stream.Position;
            }
        }

        internal AVIOContext* GetContext()
        {
            return _context;
        }
    }
}
