using FFmpeg.AutoGen;

namespace SharpAudio.FFMPEG
{
    internal unsafe struct FFmpegPointers
    {
        public AVFormatContext* format_context;
        public AVIOContext* ioContext;
        public AVStream* av_stream;
        internal avio_alloc_context_read_packet stream_read_packet;
        public SwrContext* swr_context;
        public AVPacket* av_src_packet;
        public AVFrame* av_src_frame;
        public AVFrame* av_dst_frame;
        public AVCodecContext* av_codec;
        internal avio_alloc_context_seek stream_context_seek;
    }
}
