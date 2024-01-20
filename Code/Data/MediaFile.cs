﻿using Flowframes.Data.Streams;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.Media;
using Flowframes.MiscUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stream = Flowframes.Data.Streams.Stream;

namespace Flowframes.Data
{
    public class MediaFile
    {
        public bool IsDirectory;
        public FileInfo FileInfo;
        public DirectoryInfo DirectoryInfo;
        public string Name;
        public string SourcePath;
        public string ImportPath;
        public string Format;
        public string Title;
        public string Language;
        public Fraction? InputRate = null;
        public long DurationMs;
        public int StreamCount;
        public int TotalKbits;
        public long Size;
        public List<Stream> AllStreams = new List<Stream>();
        public List<VideoStream> VideoStreams = new List<VideoStream>();
        public List<AudioStream> AudioStreams = new List<AudioStream>();
        public List<SubtitleStream> SubtitleStreams = new List<SubtitleStream>();
        public List<DataStream> DataStreams = new List<DataStream>();
        public List<AttachmentStream> AttachmentStreams = new List<AttachmentStream>();
        public VideoColorData ColorData = null;
        public long CreationTime;
        public bool Initialized = false;
        public bool SequenceInitialized = false;

        public int FileCount = 1;
        public int FrameCount { get { return VideoStreams.Count > 0 ? VideoStreams[0].FrameCount : 0; } }

        public MediaFile(string path, bool requestFpsInputIfUnset = true)
        {
            CreationTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds; // Unix Timestamp as UID

            if (IoUtils.IsPathDirectory(path))
            {
                IsDirectory = true;
                DirectoryInfo = new DirectoryInfo(path);
                Name = DirectoryInfo.Name;
                SourcePath = DirectoryInfo.FullName;
                Format = "Folder";

                if (requestFpsInputIfUnset && InputRate == null)
                    InputRate = InterpolateUtils.AskForFramerate(Name);
            }
            else
            {
                FileInfo = new FileInfo(path);
                Name = FileInfo.Name;
                SourcePath = FileInfo.FullName;
                ImportPath = FileInfo.FullName;
                Format = FileInfo.Extension.Remove(".").ToUpper();
                InputRate = new Fraction(-1, 1);
            }

            Size = GetSize();
        }

        public void InitializeSequence()
        {
            try
            {
                if (SequenceInitialized) return;

                string seqPath = Path.Combine(Paths.GetFrameSeqPath(), CreationTime.ToString(), "frames.concat");
                string chosenExt = IoUtils.GetUniqueExtensions(SourcePath).FirstOrDefault();
                int fileCount = FfmpegUtils.CreateConcatFile(SourcePath, seqPath, new List<string> { chosenExt });
                ImportPath = seqPath;
                FileCount = fileCount;
                SequenceInitialized = true;
            }
            catch (Exception e)
            {
                Logger.Log($"Error preparing frame sequence: {e.Message}\n{e.StackTrace}");
                FileCount = 0;
            }
        }

        public async Task Initialize(bool progressBar = true, bool countFrames = true)
        {
            Logger.Log($"MediaFile {Name}: Initializing", true);

            try
            {
                if (IsDirectory && !SequenceInitialized)
                    InitializeSequence();

                await LoadFormatInfo(ImportPath);
                AllStreams = await FfmpegUtils.GetStreams(ImportPath, progressBar, StreamCount, InputRate, countFrames);
                VideoStreams = AllStreams.Where(x => x.Type == Stream.StreamType.Video).Select(x => (VideoStream)x).ToList();
                AudioStreams = AllStreams.Where(x => x.Type == Stream.StreamType.Audio).Select(x => (AudioStream)x).ToList();
                SubtitleStreams = AllStreams.Where(x => x.Type == Stream.StreamType.Subtitle).Select(x => (SubtitleStream)x).ToList();
                DataStreams = AllStreams.Where(x => x.Type == Stream.StreamType.Data).Select(x => (DataStream)x).ToList();
                AttachmentStreams = AllStreams.Where(x => x.Type == Stream.StreamType.Attachment).Select(x => (AttachmentStream)x).ToList();
                Logger.Log($"Loaded and sorted streams for {Name}", true);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to initialized MediaFile: {e.Message}", true);
            }

            Initialized = true;
        }

        private async Task LoadFormatInfo(string path)
        {
            Title = await GetVideoInfo.GetFfprobeInfoAsync(path, GetVideoInfo.FfprobeMode.ShowFormat, "TAG:title");
            Language = await GetVideoInfo.GetFfprobeInfoAsync(path, GetVideoInfo.FfprobeMode.ShowFormat, "TAG:language");
            DurationMs = (await FfmpegCommands.GetDurationMs(path));
            StreamCount = await FfmpegUtils.GetStreamCount(path);
            TotalKbits = (await GetVideoInfo.GetFfprobeInfoAsync(path, GetVideoInfo.FfprobeMode.ShowFormat, "bit_rate")).GetInt() / 1000;
        }

        public string GetName()
        {
            if (IsDirectory)
                return DirectoryInfo.Name;
            else
                return FileInfo.Name;
        }

        public string GetPath()
        {
            if (IsDirectory)
                return DirectoryInfo.FullName;
            else
                return FileInfo.FullName;
        }

        public long GetSize()
        {
            try
            {
                if (IsDirectory)
                    return IoUtils.GetDirSize(GetPath(), true);
                else
                    return FileInfo.Length;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to get file size of {FileInfo.FullName}: {ex.Message} (Path Length: {FileInfo.FullName.Length})", true);
                return 0;
            }
        }

        public bool CheckFiles()
        {
            if (IsDirectory)
                return Directory.Exists(DirectoryInfo.FullName);
            else
                return File.Exists(FileInfo.FullName);
        }

        public override string ToString()
        {
            return $"{GetName()} ({FormatUtils.Bytes(Size)})";
        }
    }
}
