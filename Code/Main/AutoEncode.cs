using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Media;
using Flowframes.MiscUtils;
using Flowframes.Os;
using Flowframes.Ui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes.Main
{
    class AutoEncode
    {
        public static int chunkSize;    // Encode every n frames
        public static int safetyBufferFrames;      // Ignore latest n frames to avoid using images that haven't been fully encoded yet
        static string[] interpFramesLines;
        static List<int> encodedFrameLines = new List<int>();
        static List<int> unencodedFrameLines = new List<int>();

        static bool debug;
        public static bool busy;
        public static bool paused;

        public static void UpdateChunkAndBufferSizes()
        {
            chunkSize = GetChunkSize((IoUtils.GetAmountOfFiles(Interpolate.currentSettings.framesFolder, false, "*" + Interpolate.currentSettings.framesExt) * Interpolate.currentSettings.interpFactor).RoundToInt());

            safetyBufferFrames = 90;

            if (Interpolate.currentSettings.ai.Backend == AI.AiBackend.Ncnn)
                safetyBufferFrames = Config.GetInt(Config.Key.autoEncSafeBufferNcnn, 150);

            if (Interpolate.currentSettings.ai.Backend == AI.AiBackend.Pytorch)
                safetyBufferFrames = Config.GetInt(Config.Key.autoEncSafeBufferCuda, 90);
        }

        public static async Task MainLoop(string interpFramesPath)
        {
            if (!AutoEncodeResume.resumeNextRun)
                AutoEncodeResume.Reset();

            debug = Config.GetBool("autoEncDebug", false);

            try
            {
                UpdateChunkAndBufferSizes();

                bool imgSeq = Interpolate.currentSettings.outSettings.Encoder.GetInfo().IsImageSequence;
                //interpFramesFolder = interpFramesPath;
                string videoChunksFolder = Path.Combine(interpFramesPath.GetParentDir(), Paths.chunksDir);

                if (Interpolate.currentlyUsingAutoEnc)
                    Directory.CreateDirectory(videoChunksFolder);

                encodedFrameLines.Clear();
                unencodedFrameLines.Clear();

                Logger.Log($"[AE] Starting AutoEncode MainLoop - Chunk Size: {chunkSize} Frames - Safety Buffer: {safetyBufferFrames} Frames", true);
                int chunkNo = AutoEncodeResume.encodedChunks + 1;
                string encFile = Path.Combine(interpFramesPath.GetParentDir(), Paths.GetFrameOrderFilename(Interpolate.currentSettings.interpFactor));
                interpFramesLines = IoUtils.ReadLines(encFile).Where(x => x.StartsWith("file ")).Select(x => x.Split('/').Last().Remove("'").Split('#').First()).ToArray();     // Array with frame filenames
                if (Interpolate.currentSettings.is3D)
                {
                    // Keep only main frame lines, discard other
                    string[] singleInterpFramesLines = new string[interpFramesLines.Length / 2];
                    for (int i = 0; i < interpFramesLines.Length / 2; i++)
                        singleInterpFramesLines[i] = interpFramesLines[i * 2];
                    interpFramesLines = singleInterpFramesLines;
                }

                //while (!Interpolate.canceled && GetInterpFramesAmount() < 2)
                //    await Task.Delay(1000);

                //int lastEncodedFrameNum = 0;
                int maxUnbalance = chunkSize / 10; // Maximum ahead interpolated frames per 3D eye AI process
                int maxFrames = chunkSize + (0.5f * chunkSize).RoundToInt() + safetyBufferFrames;
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

                    //unencodedFrameLines.Clear();

                    bool aiRunning = !AiProcess.lastAiProcess.HasExited || (AiProcess.lastAiProcessOther != null && !AiProcess.lastAiProcessOther.HasExited);

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
                            AiProcessSuspend.SuspendResumeAi(true);
                        }
                        else if (!overwhelmed)
                        {
                            if (AiProcessSuspend.aiProcFrozen)
                            {
                                AiProcessSuspend.SuspendResumeAi(false);
                            }
                            else if (Interpolate.currentSettings.is3D)
                            {
                                int unbalance = InterpolationProgress.lastFrame - InterpolationProgress.lastOtherFrame;
                                if (unbalance > maxUnbalance)
                                {
                                    if (!AiProcessSuspend.IsMainSuspended())
                                        AiProcessSuspend.SuspendResumeAi(true, AiProcessSuspend.ProcessType.Main);
                                }
                                else if (unbalance < -maxUnbalance)
                                {
                                    if (!AiProcessSuspend.IsOtherSuspended())
                                        AiProcessSuspend.SuspendResumeAi(true, AiProcessSuspend.ProcessType.Other);
                                }
                                else
                                {
                                    if ((unbalance < -maxUnbalance / 5 && AiProcessSuspend.IsMainSuspended()) || (unbalance > maxUnbalance / 5 && AiProcessSuspend.IsOtherSuspended()))
                                        AiProcessSuspend.SuspendResumeAi(false);
                                }
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

                            busy = true;
                            string outpath = Path.Combine(videoChunksFolder, "chunks", $"{chunkNo.ToString().PadLeft(4, '0')}{FfmpegUtils.GetExt(Interpolate.currentSettings.outSettings)}");
                            string firstFile = Path.GetFileName(interpFramesLines[frameLinesToEncode.First()].Trim());
                            string lastFile = Path.GetFileName(interpFramesLines[frameLinesToEncode.Last()].Trim());
                            Logger.Log($"[AE] Encoding Chunk #{chunkNo} to using line {frameLinesToEncode.First()} ({firstFile}) through {frameLinesToEncode.Last()} ({lastFile}) - {unencodedFrameLines.Count} unencoded frames left in total", true, false, "ffmpeg");

                            //await Export.EncodeChunk(outpath, Interpolate.currentSettings.interpFolder, chunkNo, Interpolate.currentSettings.outSettings, frameLinesToEncode.First(), frameLinesToEncode.Count);
                            int currentChunkNo = chunkNo; // capture value
                            int firstLineToEncode = frameLinesToEncode.First(); // capture value
                            int noLinesToEncode = frameLinesToEncode.Count; // capture value
                            currentEncodingTask = Task.Run(async () =>
                            {
                                await Export.EncodeChunk(outpath, Interpolate.currentSettings.interpFolder, currentChunkNo, Interpolate.currentSettings.outSettings, firstLineToEncode, noLinesToEncode);

                                if (Interpolate.canceled) return;

                                if (aiRunning && Config.GetInt(Config.Key.autoEncMode) == 2)
                                    DeleteOldFrames(interpFramesPath, frameLinesToEncode);

                                busy = false;
                            });

                            if (Interpolate.canceled) return;

                            encodedFrameLines.AddRange(frameLinesToEncode);
                            unencodedFrameLines.RemoveRange(0, frameLinesToEncode.Count);
                            Logger.Log("[AE] Done Encoding Chunk #" + chunkNo, true, false, "ffmpeg");
                            //lastEncodedFrameNum = (frameLinesToEncode.Last() + 1);
                            chunkNo++;
                            AutoEncodeResume.Save();

                            if (!imgSeq && Config.GetInt(Config.Key.autoEncBackupMode) > 0)
                            {
                                if (aiRunning && (currentMuxTask == null || (currentMuxTask != null && currentMuxTask.IsCompleted)))
                                    currentMuxTask = Task.Run(() => Export.ChunksToVideo(Interpolate.currentSettings.tempFolder, videoChunksFolder, Interpolate.currentSettings.outPath, true));
                                else
                                    Logger.Log($"[AE] Skipping backup because {(!aiRunning ? "this is the final chunk" : "previous mux task has not finished yet")}!", true, false, "ffmpeg");
                            }

                            //busy = false;
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

                if (imgSeq)
                    return;

                await Export.ChunksToVideo(Interpolate.currentSettings.tempFolder, videoChunksFolder, Interpolate.currentSettings.outPath);
            }
            catch (Exception e)
            {
                Logger.Log($"AutoEnc Error: {e.Message}. Stack Trace:\n{e.StackTrace}");
                Interpolate.Cancel("Auto-Encode encountered an error.");
            }
        }

        static void DeleteOldFrames(string interpFramesPath, List<int> frameLinesToEncode)
        {
            if (debug)
                Logger.Log("[AE] Starting DeleteOldFramesAsync.", true, false, "ffmpeg");

            Stopwatch sw = new Stopwatch();
            sw.Restart();

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
            //if (Interpolate.canceled || interpFramesFolder == null) return false;
            if (Interpolate.canceled) return false;
            bool processRunning = (AiProcess.lastAiProcess != null && !AiProcess.lastAiProcess.HasExited) || (AiProcess.lastAiProcessOther != null && !AiProcess.lastAiProcessOther.HasExited);
            if (debug)
                Logger.Log($"[AE] HasWorkToDo - Process Running: {processRunning} - encodedFrameLines.Count: {encodedFrameLines.Count} - interpFramesLines.Length: {interpFramesLines.Length}", true);

            return processRunning || encodedFrameLines.Count < interpFramesLines.Length;
        }

        static int GetChunkSize(int targetFramesAmount)
        {
            /*if (targetFramesAmount > 100000) return 4800;
            if (targetFramesAmount > 50000) return 2400;
            if (targetFramesAmount > 20000) return 1200;
            if (targetFramesAmount > 5000) return 600;
            if (targetFramesAmount > 1000) return 300;
            return 150;*/
            int round = (int)Math.Floor(targetFramesAmount / 2400f);
            if (round == 0)
                round = 1;
            return Math.Min(round * 600, 6000);
        }

        //static int GetInterpFramesAmount()
        //{
        //    return IoUtils.GetAmountOfFiles(interpFramesFolder, false, "*" + Interpolate.currentSettings.interpExt);
        //}
    }
}
