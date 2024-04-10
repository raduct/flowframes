using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Media;
using Flowframes.MiscUtils;
using Flowframes.Os;
using Flowframes.Ui;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Flowframes.Main
{
    class AutoEncode
    {
        public static int chunkSize;    // Encode every n frames
        public static int safetyBufferFrames;      // Ignore latest n frames to avoid using images that haven't been fully encoded yet
        static string[] interpFramesLines;
        static readonly List<int> encodedFrameLines = new List<int>();
        static readonly List<int> unencodedFrameLines = new List<int>();

        static bool debug;
        public static bool busy;
        public static bool paused;

        public static void UpdateChunkAndBufferSizes()
        {
            chunkSize = GetChunkSize(IoUtils.GetAmountOfFiles(Path.Combine(Interpolate.currentSettings.tempFolder, Paths.framesWorkDir), false, "*" + Interpolate.currentSettings.framesExt), Interpolate.currentSettings.interpFactor);

            safetyBufferFrames = 90;

            if (Interpolate.currentSettings.ai.Backend == AI.AiBackend.Ncnn)
                safetyBufferFrames = Config.GetInt(Config.Key.autoEncSafeBufferNcnn, 150);

            if (Interpolate.currentSettings.ai.Backend == AI.AiBackend.Pytorch)
                safetyBufferFrames = Config.GetInt(Config.Key.autoEncSafeBufferCuda, 90);
        }

        public static async Task MainLoop(string interpFramesPath)
        {
            paused = false;
            busy = false;
            debug = Config.GetBool("autoEncDebug", false);

            try
            {
                UpdateChunkAndBufferSizes();

                bool imgSeq = Interpolate.currentSettings.outSettings.Encoder.GetInfo().IsImageSequence;
                string videoChunksFolder = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.chunksDir);

                if (Interpolate.currentlyUsingAutoEnc)
                    Directory.CreateDirectory(videoChunksFolder);

                encodedFrameLines.Clear();
                unencodedFrameLines.Clear();

                Logger.Log($"[AE] Starting AutoEncode MainLoop - Chunk Size: {chunkSize} Frames - Safety Buffer: {safetyBufferFrames} Frames", true);
                int chunkNo = AutoEncodeResume.encodedChunks + 1;
                string encFile = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.GetFrameOrderFilename(Interpolate.currentSettings.interpFactor));
                interpFramesLines = IoUtils.ReadLines(encFile).Where(x => x.StartsWith("file ")).Select(x => x.Split('/').Last().Remove("' ").Split('#').First()).ToArray();     // Array with frame filenames
                if (Interpolate.currentSettings.is3D)
                {
                    // Keep only main frame lines, discard other
                    string[] singleInterpFramesLines = new string[interpFramesLines.Length / 2];
                    for (int i = 0; i < interpFramesLines.Length / 2; i++)
                        singleInterpFramesLines[i] = interpFramesLines[i * 2];
                    interpFramesLines = singleInterpFramesLines;
                }

                int maxUnbalance = chunkSize / 10; // Maximum ahead interpolated frames per 3D eye AI process
                int maxFrames = 3 * chunkSize + safetyBufferFrames;
                Task currentMuxTask = null;
                Task currentEncodingTask = null;

                while (HasWorkToDo())    // Loop while proc is running and not all frames have been encoded
                {
                    if (Interpolate.canceled) return;

                    if (paused || InterpolationProgress.lastFrame == 0 || (Interpolate.currentSettings.is3D && InterpolationProgress.lastOtherFrame == 0))
                    {
                        await Task.Delay(200);
                        continue;
                    }

                    bool aiRunning = AiProcess.IsRunning();

                    string lastFrame = (Interpolate.currentSettings.is3D ? Math.Min(InterpolationProgress.lastFrame, InterpolationProgress.lastOtherFrame) : InterpolationProgress.lastFrame).ToString().PadLeft(Padding.interpFrames, '0');
                    for (int frameLineNum = unencodedFrameLines.Count > 0 ? unencodedFrameLines.Last() + 1 : 0; frameLineNum < interpFramesLines.Length; frameLineNum++)
                    {
                        if (aiRunning && interpFramesLines[frameLineNum].Contains(lastFrame))
                            break;

                        unencodedFrameLines.Add(frameLineNum);
                    }

                    if (aiRunning && Config.GetBool(Config.Key.alwaysWaitForAutoEnc))
                    {
                        bool overwhelmed = unencodedFrameLines.Count > maxFrames;

                        if (overwhelmed && !AiProcessSuspend.aiProcFrozen && OsUtils.IsProcessHidden(AiProcess.lastAiProcess))
                        {
                            Logger.Log($"AutoEnc is overwhelmed! ({unencodedFrameLines.Count} unencoded frames > {maxFrames}) - Pausing.", true);
                            AiProcessSuspend.Suspend();
                        }
                        else if (!overwhelmed)
                        {
                            if (AiProcessSuspend.aiProcFrozen)
                            {
                                AiProcessSuspend.Resume();
                            }
                            else if (Interpolate.currentSettings.is3D)
                            {
                                int unbalance = InterpolationProgress.lastFrame - InterpolationProgress.lastOtherFrame;
                                if (unbalance > maxUnbalance)
                                    AiProcessSuspend.Suspend(AiProcessSuspend.ProcessType.Main);
                                else if (unbalance < -maxUnbalance)
                                    AiProcessSuspend.Suspend(AiProcessSuspend.ProcessType.Other);
                                else if ((unbalance <= 0) && !AiProcessSuspend.IsMainRunning() || (unbalance >= 0) && !AiProcessSuspend.IsOtherRunning())
                                    AiProcessSuspend.Resume();
                            }
                        }
                    }

                    if ((currentEncodingTask == null || currentEncodingTask.IsCompleted) && (unencodedFrameLines.Count >= (chunkSize + safetyBufferFrames) || (unencodedFrameLines.Count > 0 && !aiRunning)))     // Encode every n frames, or after process has exited
                    {
                        try
                        {
                            List<int> frameLinesToEncode = aiRunning ? unencodedFrameLines.Take(chunkSize).ToList() : unencodedFrameLines;     // Take all remaining frames if process is done
                            string lastOfChunk = Path.Combine(interpFramesPath, interpFramesLines[frameLinesToEncode.Last()]);
                            string lastOfChunkOther = Path.Combine(Paths.GetOtherDir(interpFramesPath), interpFramesLines[frameLinesToEncode.Last()]);

                            if (!File.Exists(lastOfChunk) || (Interpolate.currentSettings.is3D && !File.Exists(lastOfChunkOther)))
                            {
                                if (debug)
                                    Logger.Log($"[AE] Last frame of chunk doesn't exist; skipping loop iteration ({frameLinesToEncode.Last()})", true);

                                await Task.Delay(500);
                                continue;
                            }

                            string outpath = Path.Combine(videoChunksFolder, "chunks", $"{chunkNo.ToString().PadLeft(4, '0')}{FfmpegUtils.GetExt(Interpolate.currentSettings.outSettings)}");
                            string firstFile = Path.GetFileName(interpFramesLines[frameLinesToEncode.First()]);
                            string lastFile = Path.GetFileName(interpFramesLines[frameLinesToEncode.Last()]);
                            Logger.Log($"[AE] Encoding Chunk #{chunkNo} to using line {frameLinesToEncode.First()} ({firstFile}) through {frameLinesToEncode.Last()} ({lastFile}) - {unencodedFrameLines.Count} unencoded frames left in total", true, false, "ffmpeg");

                            busy = true;
                            int currentChunkNo = chunkNo; // capture value
                            int[] frameLinesToEncodeAr = frameLinesToEncode.ToArray(); // capture value
                            currentEncodingTask = Task.Run(async () =>
                            {
                                await Export.EncodeChunk(outpath, Interpolate.currentSettings.interpFolder, currentChunkNo, Interpolate.currentSettings.outSettings, frameLinesToEncodeAr[0], frameLinesToEncodeAr.Length, aiRunning ? AvProcess.LogMode.Background : AvProcess.LogMode.OnlyLastLine);

                                IEnumerable<string> inputFrames = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(encFile + FrameOrder.inputFramesJson)).Take(frameLinesToEncodeAr[0] + frameLinesToEncodeAr.Length);
                                int indexInputFrame = inputFrames.Distinct().Count();
                                AutoEncodeResume.encodedChunks++;
                                AutoEncodeResume.encodedFrames += frameLinesToEncodeAr.Length;
                                AutoEncodeResume.lastEncodedOriginalInputFrame = FrameRename.GetOriginalFileName(indexInputFrame - 1);
                                AutoEncodeResume.SaveChunk();

                                if (Interpolate.canceled) return;

                                if (Config.GetInt(Config.Key.autoEncMode) == 2)
                                    DeleteOldFrames(interpFramesPath, frameLinesToEncodeAr);

                                Logger.Log("[AE] Done Encoding Chunk #" + currentChunkNo, true, false, "ffmpeg");
                                busy = false;
                            });

                            if (Interpolate.canceled) return;

                            encodedFrameLines.AddRange(frameLinesToEncode);
                            unencodedFrameLines.RemoveRange(0, frameLinesToEncode.Count);
                            chunkNo++;

                            if (!imgSeq && Config.GetInt(Config.Key.autoEncBackupMode) > 0)
                            {
                                if (aiRunning && (currentMuxTask == null || currentMuxTask.IsCompleted))
                                    currentMuxTask = Task.Run(() => Export.ChunksToVideo(Interpolate.currentSettings.tempFolder, videoChunksFolder, Interpolate.currentSettings.outPath, true));
                                else
                                    Logger.Log($"[AE] Skipping backup because {(!aiRunning ? "this is the final chunk" : "previous mux task has not finished yet")}!", true, false, "ffmpeg");
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Log($"AutoEnc Chunk Encoding Error: {e.Message}. Stack Trace:\n{e.StackTrace}");
                            Interpolate.Cancel("Auto-Encode encountered an error.");
                        }
                    }

                    await Task.Delay(500);
                }

                if (Interpolate.canceled) return;

                if (currentEncodingTask != null)
                    await currentEncodingTask;

                if (currentMuxTask != null)
                    await currentMuxTask;

                if (!imgSeq)
                    await Export.ChunksToVideo(Interpolate.currentSettings.tempFolder, videoChunksFolder, Interpolate.currentSettings.outPath);
            }
            catch (Exception e)
            {
                Logger.Log($"AutoEnc Error: {e.Message}. Stack Trace:\n{e.StackTrace}");
                Interpolate.Cancel("Auto-Encode encountered an error.");
            }
        }

        static void DeleteOldFrames(string interpFramesPath, int[] frameLinesToEncode)
        {
            if (debug)
                Logger.Log("[AE] Starting DeleteOldFramesAsync.", true, false, "ffmpeg");

            Stopwatch sw = Stopwatch.StartNew();

            foreach (int frame in frameLinesToEncode)
            {
                if (!FrameIsStillNeeded(interpFramesLines[frame], frame))    // Make sure frames are no longer needed (for dupes) before deleting!
                {
                    string framePath = Path.Combine(interpFramesPath, interpFramesLines[frame]);
                    //IOUtils.OverwriteFileWithText(framePath);    // Overwrite to save space without breaking progress counter
                    IoUtils.TryDeleteIfExists(framePath);
                    if (Interpolate.currentSettings.is3D)
                        IoUtils.TryDeleteIfExists(Path.Combine(Paths.GetOtherDir(interpFramesPath), interpFramesLines[frame]));
                    InterpolationProgress.deletedFramesCount++;
                }
            }

            if (debug)
                Logger.Log("[AE] DeleteOldFramesAsync finished in " + FormatUtils.TimeSw(sw), true, false, "ffmpeg");
        }

        static bool FrameIsStillNeeded(string frameName, int frameIndex)
        {
            if ((frameIndex + 1) < interpFramesLines.Length && interpFramesLines[frameIndex + 1].Contains(frameName))
                return true;
            return false;
        }

        public static bool HasWorkToDo()
        {
            if (Interpolate.canceled) return false;
            bool aiRunning = AiProcess.IsRunning();
            if (debug)
                Logger.Log($"[AE] HasWorkToDo - Process Running: {aiRunning} - encodedFrameLines.Count: {encodedFrameLines.Count} - interpFramesLines.Length: {interpFramesLines.Length}", true);

            return aiRunning || encodedFrameLines.Count < interpFramesLines.Length;
        }

        static int GetChunkSize(int inuptFramesNo, float factor)
        {
            int intMFactor = (factor * 1000).RoundToInt();
            int factorMultiple = Math.Truncate(factor) == factor ? (int)factor : intMFactor / (int)BigInteger.GreatestCommonDivisor(1000, intMFactor);

            // multiple of 600 (for video I frame interval) and factor (for resume)
            int multiple = 600 * factorMultiple / (int)BigInteger.GreatestCommonDivisor(600, factorMultiple);
            int round = Math.Min(10, (int)Math.Ceiling(inuptFramesNo * factor / multiple / 5));
            return round * multiple;
        }
    }
}
