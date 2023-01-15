﻿using Flowframes.Data;
using Flowframes.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using static Flowframes.Data.Enums.Encoding;
using Encoder = Flowframes.Data.Enums.Encoding.Encoder;
using PixFmt = Flowframes.Data.Enums.Encoding.PixelFormat;

namespace Flowframes.MiscUtils
{
    internal class OutputUtils
    {
        public static EncoderInfoVideo GetEncoderInfoVideo(Encoder encoder)
        {
            if (encoder == Encoder.X264)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.H264,
                    Name = "libx264",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P },
                };
            }

            if (encoder == Encoder.X265)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.H265,
                    Name = "libx265",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P, PixFmt.Yuv420P10Le, PixFmt.Yuv444P10Le },
                };
            }

            if (encoder == Encoder.Nvenc264)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.H264,
                    Name = "h264_nvenc",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P },
                    HwAccelerated = true,
                };
            }

            if (encoder == Encoder.SvtAv1)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.AV1,
                    Name = "libsvtav1",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv420P10Le },
                    PixelFormatDefault = PixFmt.Yuv420P10Le,
                    MaxFramerate = 240,
                };
            }

            if (encoder == Encoder.VpxVp9)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.VP9,
                    Name = "libvpx-vp9",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P, PixFmt.Yuv420P10Le, PixFmt.Yuv444P, PixFmt.Yuv444P10Le },
                };
            }

            if (encoder == Encoder.Nvenc265)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.H265,
                    Name = "hevc_nvenc",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P, PixFmt.Yuv420P10Le },
                    HwAccelerated = true,
                };
            }

            if (encoder == Encoder.NvencAv1)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.AV1,
                    Name = "av1_nvenc",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P, PixFmt.Yuv420P10Le },
                    PixelFormatDefault = PixFmt.Yuv420P10Le,
                    HwAccelerated = true,
                };
            }

            if (encoder == Encoder.ProResKs)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.ProRes,
                    Name = "prores_ks",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv422P10Le, PixFmt.Yuv444P10Le, PixFmt.Yuva444P10Le },
                };
            }

            if (encoder == Encoder.Gif)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Gif,
                    Name = "gif",
                    PixelFormats = new List<PixFmt>() { PixFmt.Rgb8 },
                    OverideExtension = "gif",
                    MaxFramerate = 50,
                };
            }

            if (encoder == Encoder.Ffv1)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Ffv1,
                    Name = "ffv1",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv444P, PixFmt.Yuv422P, PixFmt.Yuv422P, PixFmt.Yuv420P10Le, PixFmt.Yuv444P10Le },
                    Lossless = true,
                };
            }

            if (encoder == Encoder.Huffyuv)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Huffyuv,
                    Name = "huffyuv",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv422P, PixFmt.Rgb24 },
                    Lossless = true,
                };
            }

            if (encoder == Encoder.Magicyuv)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Magicyuv,
                    Name = "magicyuv",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv422P, PixFmt.Yuv444P },
                    Lossless = true,
                };
            }

            if (encoder == Encoder.Rawvideo)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Rawvideo,
                    Name = "rawvideo",
                    Lossless = true,
                };
            }

            if (encoder == Encoder.Png)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Png,
                    Name = "png",
                    PixelFormats = new List<PixFmt>() { PixFmt.Rgb24, PixFmt.Rgba },
                    Lossless = true,
                    IsImageSequence = true,
                    OverideExtension = "png",
                };
            }

            if (encoder == Encoder.Jpeg)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Jpeg,
                    Name = "mjpeg",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuv422P, PixFmt.Yuv444P },
                    IsImageSequence = true,
                    OverideExtension = "jpg",
                };
            }

            if (encoder == Encoder.Webp)
            {
                return new EncoderInfoVideo
                {
                    Codec = Codec.Webp,
                    Name = "libwebp",
                    PixelFormats = new List<PixFmt>() { PixFmt.Yuv420P, PixFmt.Yuva420P },
                    IsImageSequence = true,
                    OverideExtension = "webp",
                };
            }

            return new EncoderInfoVideo();
        }

        public static List<Codec> GetSupportedCodecs(Enums.Output.Format format)
        {
            switch(format)
            {
                case Enums.Output.Format.Mp4: return new List<Codec> { Codec.H264, Codec.H265, Codec.AV1 };
                case Enums.Output.Format.Mkv: return new List<Codec> { Codec.H264, Codec.H265, Codec.AV1, Codec.VP9 };
                case Enums.Output.Format.Webm: return new List<Codec> { Codec.VP9, Codec.AV1 };
                case Enums.Output.Format.Mov: return new List<Codec> { Codec.ProRes };
                case Enums.Output.Format.Avi: return new List<Codec> { Codec.Ffv1, Codec.Huffyuv, Codec.Magicyuv, Codec.Rawvideo };
                case Enums.Output.Format.Gif: return new List<Codec> { Codec.Gif };
                case Enums.Output.Format.Images: return new List<Codec> { Codec.Png, Codec.Jpeg, Codec.Webp };
                case Enums.Output.Format.Realtime: return new List<Codec> { };
                default: return new List<Codec> { };
            }
        }

        public static List<Encoder> GetAvailableEncoders(Enums.Output.Format format)
        {
            var allEncoders = Enum.GetValues(typeof(Encoder)).Cast<Encoder>();
            var supported = GetSupportedCodecs(format);
            return allEncoders.Where(e => supported.Contains(GetEncoderInfoVideo(e).Codec)).ToList();
        }
    }
}