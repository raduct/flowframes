using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Os;
using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Flowframes.Magick
{
    class Dedupe
    {
        public const string dupesFileName = "dupes.json";

        public static async Task Run(string path, bool testRun = false, bool setStatus = true)
        {
            if (path == null || !Directory.Exists(path) || Interpolate.canceled)
                return;

            if (setStatus)
                Program.mainForm.SetStatus("Running frame de-duplication");

            float currentThreshold = Config.GetFloat(Config.Key.dedupThresh);
            Logger.Log("Running accurate frame de-duplication...");

            await RemoveDupeFrames(path, currentThreshold, "*", testRun, false);
        }

        static MagickImage GetImage(string path)
        {
            return new MagickImage(path);
        }

        public static async Task RemoveDupeFrames(string path, float threshold, string ext, bool testRun = false, bool debugLog = false)
        {
            Logger.Log($"Removing duplicate frames - Threshold: {threshold:0.00}");

            FileInfo[] framePaths = IoUtils.GetFileInfosSorted(path, false, "*." + ext);
            ConcurrentDictionary<string, int> framesToDelete = new ConcurrentDictionary<string, int>();

            int statsFramesKept = framePaths.Length > 0 ? 1 : 0; // always keep at least one frame
            int statsFramesDeleted = 0;
            string testStr = testRun ? "[TESTRUN] " : "";

            int increment = Interpolate.currentSettings.is3D ? 2 : 1;

            void lamProcessFrames(int indStart, int indEnd)
            {
                MagickImage img1 = null;
                MagickImage img2 = null;

                for (int i = indStart; i < indEnd; i += increment)     // Loop through frames
                {
                    string frame1_name = framePaths[i].FullName;

                    // its likely we carried over an already loaded image from a previous iteration
                    if (img1 == null || img1.FileName != frame1_name)
                        img1 = GetImage(framePaths[i].FullName);

                    if (img1 == null) continue;

                    for (int j = i + increment; j < framePaths.Length; j += increment)
                    {
                        if (Interpolate.canceled) return;

                        string frame2_name = framePaths[j].FullName;

                        if (j >= indEnd)
                        {
                            // if we are already extending outside of this thread's range and j is already flagged, then we need to abort
                            if (framesToDelete.ContainsKey(frame2_name))
                                return;
                        }

                        img2 = GetImage(framePaths[j].FullName);
                        if (img2 == null) continue;

                        float diff = GetDifference(img1, img2);

                        if (diff < threshold)     // Is a duped frame.
                        {
                            framesToDelete[frame2_name] = 0;
                            if (Interpolate.currentSettings.is3D)
                                framesToDelete[framePaths[j + 1].FullName] = 0;
                            if (debugLog)
                                Logger.Log($"{testStr}Deduplication: Deleted {Path.GetFileName(frame2_name)}");

                            Interlocked.Increment(ref statsFramesDeleted);

                            if (j + increment >= framePaths.Length)
                                return;

                            continue; // test next frame
                        }

                        Interlocked.Increment(ref statsFramesKept);

                        // this frame is different, stop testing agaisnt 'i'
                        // all the frames between i and j are dupes, we can skip them
                        i = j - increment;
                        // keep the currently loaded in img for the next iteration
                        img1 = img2;
                        break;
                    }
                }
            }

            void lamUpdateInfoBox()
            {
                int framesProcessed = statsFramesKept + statsFramesDeleted;
                Logger.Log($"Deduplication: Running de-duplication ({framesProcessed}/{framePaths.Length / increment}), deleted {statsFramesDeleted} ({(float)statsFramesDeleted / framePaths.Length / increment * 100f:0}%) duplicate frames so far...", false, true);
                Program.mainForm.SetProgress(((float)framesProcessed / framePaths.Length / increment * 100f).RoundToInt());
            }

            // start the worker threads
            Task[] workTasks = new Task[Environment.ProcessorCount];
            int chunkSize = framePaths.Length / workTasks.Length / 2 * 2; // make sure is even
            for (int i = 0; i < workTasks.Length; i++)
            {
                int indStart = chunkSize * i;
                int indEnd = indStart + chunkSize;
                if (i + 1 == workTasks.Length) indEnd = framePaths.Length;

                workTasks[i] = Task.Run(() => lamProcessFrames(indStart, indEnd));
            }

            // wait for all the worker threads to finish and update the info box
            while (!Task.WaitAll(workTasks, 250))
            {
                await Task.CompletedTask;
                lamUpdateInfoBox(); // Print every 0.25s (or when done)
            }
            lamUpdateInfoBox();

            if (Interpolate.canceled) return;

            if (!testRun)
                foreach (string frame in framesToDelete.Keys)
                    IoUtils.TryDeleteIfExists(frame);

            float percentDeleted = (float)statsFramesDeleted / framePaths.Length / increment * 100f;
            string keptPercent = $"{100f - percentDeleted:0.0}%";

            if (statsFramesDeleted <= 0)
            {
                Logger.Log($"Deduplication: No duplicate frames detected on this video.", false, true);
            }
            else if (statsFramesKept <= 0)
            {
                Interpolate.Cancel("No frames were left after de-duplication!\n\nTry lowering the de-duplication threshold.");
            }
            else
            {
                Logger.Log($"{testStr}Deduplication: Kept {statsFramesKept} ({keptPercent}) frames, deleted {statsFramesDeleted} frames.", false, true);
            }
        }

        static float GetDifference(MagickImage img1, MagickImage img2)
        {
            double err = img1.Compare(img2, ErrorMetric.Fuzz);
            float errPercent = (float)err * 100f;
            return errPercent;
        }

        public static void CreateDupesFile(string framesPath, string ext)
        {
            bool debug = Config.GetBool("dupeScanDebug", false);

            FileInfo[] frameFiles = IoUtils.GetFileInfosSorted(framesPath, false, "*" + ext);

            if (debug)
                Logger.Log($"Running CreateDupesFile for '{framesPath}' ({frameFiles.Length} files), ext = {ext}.", true, false, "dupes");

            Dictionary<string, List<string>> frames = new Dictionary<string, List<string>>();

            for (int i = 0; i < frameFiles.Length; i++)
            {
                string curFrameName = Path.GetFileNameWithoutExtension(frameFiles[i].Name);
                int curFrameNo = curFrameName.GetInt();

                List<string> dupes = new List<string>();
                if ((i + 1) < frameFiles.Length)
                {
                    string nextFrameName = Path.GetFileNameWithoutExtension(frameFiles[i + 1].Name);
                    int nextFrameNo = nextFrameName.GetInt();

                    for (int j = curFrameNo + 1; j < nextFrameNo; j++)
                    {
                        dupes.Add(j.ToString().PadLeft(Padding.inputFrames, '0'));
                    }
                }
                frames[curFrameName] = dupes;
            }

            File.WriteAllText(Path.Combine(framesPath.GetParentDir(), dupesFileName), frames.ToJson(true));
        }

        public static async Task CreateFramesFileVideo(string videoPath, bool loop)
        {
            if (!Directory.Exists(Interpolate.currentSettings.tempFolder))
                Directory.CreateDirectory(Interpolate.currentSettings.tempFolder);

            Process ffmpeg = OsUtils.NewProcess(true);
            string baseCmd = $"/C cd /D {Path.Combine(IO.Paths.GetPkgPath(), IO.Paths.audioVideoDir).Wrap()}";
            string mpDec = FfmpegCommands.GetMpdecimate((int)FfmpegCommands.MpDecSensitivity.Normal, false);
            ffmpeg.StartInfo.Arguments = $"{baseCmd} & ffmpeg -loglevel debug -y -i {videoPath.Wrap()} -fps_mode vfr -vf {mpDec} -f null NUL 2>&1 | findstr keep_count:";
            List<string> ffmpegOutputLines = (await Task.Run(() => OsUtils.GetProcStdOut(ffmpeg, true))).SplitIntoLines().Where(l => l.IsNotEmpty()).ToList();

            var frames = new Dictionary<int, List<int>>();
            var frameNums = new List<int>();
            int lastKeepFrameNum = 0;

            for (int frameIdx = 0; frameIdx < ffmpegOutputLines.Count; frameIdx++)
            {
                string line = ffmpegOutputLines[frameIdx];
                bool drop = frameIdx != 0 && line.Contains(" drop ") && !line.Contains(" keep ");
                // Console.WriteLine($"[Frame {frameIdx.ToString().PadLeft(6, '0')}] {(drop ? "DROP" : "KEEP")}");
                // frameNums.Add(lastKeepFrameNum);

                if (!drop)
                {
                    if (!frames.TryGetValue(frameIdx, out List<int> value) || value == null)
                    {
                        frames[frameIdx] = new List<int>();
                    }

                    lastKeepFrameNum = frameIdx;
                }
                else
                {
                    frames[lastKeepFrameNum].Add(frameIdx);
                }
            }

            var inputFrames = new List<int>(frames.Keys);

            if (loop)
            {
                inputFrames.Add(inputFrames.First());
            }

            File.WriteAllText(Path.Combine(Interpolate.currentSettings.tempFolder, "input.json"), inputFrames.ToJson(true));
            File.WriteAllText(Path.Combine(Interpolate.currentSettings.tempFolder, "dupes.test.json"), frames.ToJson(true));
        }
    }
}
