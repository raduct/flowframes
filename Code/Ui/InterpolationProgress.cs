using Flowframes.Forms;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.MiscUtils;
using Flowframes.Os;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using I = Flowframes.Interpolate;

namespace Flowframes.Ui
{
    class InterpolationProgress
    {
        public static int deletedFramesCount;
        public static int lastFrame;
        public static int lastOtherFrame;
        public static int targetFrames;
        public static float currentFactor;
        public static bool progCheckRunning = false;

        public static PictureBox preview;
        public static BigPreviewForm bigPreviewForm;

        public static void Restart()
        {
            progCheckRunning = true;
            deletedFramesCount = 0;
            lastFrame = lastOtherFrame = 0;
            peakFpsOut = 0f;
            Program.mainForm.SetProgress(0);
        }

        public static async void GetProgressByFrameAmount(string outdir, int target)
        {
            targetFrames = target;
            string currentOutdir = outdir;
            Restart();
            Logger.Log($"Starting GetProgressByFrameAmount() loop for outdir '{currentOutdir}', target is {target} frames", true);
            bool firstProgUpd = true;

            while (Program.busy)
            {
                if (AiProcess.processTime.IsRunning && Directory.Exists(currentOutdir))
                {
                    if (firstProgUpd && Program.mainForm.IsInFocus())
                        Program.mainForm.SetTab(Program.mainForm.previewTab.Name);

                    firstProgUpd = false;
                    int lastFrameNo = I.currentSettings.is3D ? Math.Min(lastFrame, lastOtherFrame) : lastFrame;
                    string lastFramePath = currentOutdir + "\\" + lastFrameNo.ToString().PadLeft(Data.Padding.interpFrames, '0') + I.currentSettings.interpExt;

                    if (lastFrameNo > 1)
                        UpdateInterpProgress(lastFrameNo, targetFrames, lastFramePath);

                    await Task.Delay((target < 1000) ? 200 : 1000);

                    if (lastFrameNo >= targetFrames)
                        break;
                }
                else
                {
                    await Task.Delay(200);
                }
            }

            progCheckRunning = false;

            if (I.canceled)
                Program.mainForm.SetProgress(0);
        }

        public static void UpdateLastFrameFromInterpOutput(string output)
        {
            try
            {
                string ncnnStr = I.currentSettings.ai.NameInternal.Contains("NCNN") ? " done" : "";
                Regex frameRegex = new Regex($@"\d*(?={I.currentSettings.interpExt}{ncnnStr})");
                Match result = frameRegex.Match(output);
                if (result.Success)
                {
                    int frame = int.Parse(result.Value);
                    if (I.currentSettings.is3D)
                    {
                        Regex interpRegex = new Regex($@"{Paths.interpDir}/(?=\d*{I.currentSettings.interpExt}{ncnnStr})");
                        if (interpRegex.IsMatch(output))
                            lastFrame = Math.Max(int.Parse(result.Value), lastFrame);
                        else
                            lastOtherFrame = Math.Max(int.Parse(result.Value), lastOtherFrame);
                    }
                    else
                        lastFrame = Math.Max(frame, lastFrame);
                }
            }
            catch
            {
                Logger.Log($"UpdateLastFrameFromInterpOutput: Failed to get progress from '{output}' even though Regex matched!", true);
            }
        }

        public static async void GetProgressFromFfmpegLog(string logFile, int target)
        {
            targetFrames = target;
            Restart();
            Logger.Log($"Starting GetProgressFromFfmpegLog() loop for log '{logFile}', target is {target} frames", true);
            UpdateInterpProgress(0, targetFrames);

            while (Program.busy)
            {
                if (AiProcess.processTime.IsRunning)
                {
                    string lastLogLine = Logger.GetSessionLogLastLine(logFile);
                    int num = lastLogLine == null ? 0 : lastLogLine.Split("frame=")[1].Split("fps=")[0].GetInt();

                    if (num > 0)
                        UpdateInterpProgress(num, targetFrames);

                    await Task.Delay(500);

                    if (num >= targetFrames)
                        break;
                }
                else
                {
                    await Task.Delay(100);
                }
            }

            progCheckRunning = false;

            if (I.canceled)
                Program.mainForm.SetProgress(0);
        }

        //public static int interpolatedInputFramesCount;
        public static float peakFpsOut;

        private const int previewUpdateRateMs = 200;
        private static readonly Regex EOLRegex = new Regex("\r\n|\r|\n");

        public static void UpdateInterpProgress(int frames, int target, string latestFramePath = "")
        {
            if (I.canceled) return;
            //interpolatedInputFramesCount = ((frames / I.currentSettings.interpFactor).RoundToInt() - 1);
            //ResumeUtils.Save();
            target = (target / I.InterpProgressMultiplier).RoundToInt();
            frames = frames.Clamp(0, target);
            int percent = (int)Math.Round((float)frames / target * 100f);
            Program.mainForm.SetProgress(percent);

            float generousTime = (AiProcess.processTime.ElapsedMilliseconds - AiProcess.lastStartupTimeMs) / 1000f;
            float fps = ((float)frames / generousTime).Clamp(0, 9999);
            string fpsIn = (fps / currentFactor).ToString("0.00");
            string fpsOut = fps.ToString("0.00");

            if (fps > peakFpsOut)
                peakFpsOut = fps;

            float secondsPerFrame = generousTime / frames;
            int framesLeft = target - frames;
            float eta = framesLeft * secondsPerFrame;
            string etaStr = FormatUtils.Time(new TimeSpan(0, 0, eta.RoundToInt()), false);

            bool replaceLine = EOLRegex.Split(Logger.textbox.Text).Last().Contains("Average Speed: ");

            string logStr = $"Interpolated {frames}/{target} Frames ({percent}%) - Average Speed: {fpsIn} FPS In / {fpsOut} FPS Out - ";
            logStr += $"Time: {FormatUtils.Time(AiProcess.processTime.Elapsed)} - ETA: {etaStr}";
            if (AutoEncode.busy) logStr += " - Encoding...";
            Logger.Log(logStr, false, replaceLine);
            try
            {
                if (!string.IsNullOrWhiteSpace(latestFramePath) && frames > currentFactor)
                {
                    if (bigPreviewForm == null && !preview.Visible  /* ||Program.mainForm.WindowState != FormWindowState.Minimized */ /* || !Program.mainForm.IsInFocus()*/) return;        // Skip if the preview is not visible or the form is not in focus
                    if (timeSinceLastPreviewUpdate.IsRunning && timeSinceLastPreviewUpdate.ElapsedMilliseconds < previewUpdateRateMs) return;
                    Image img = IoUtils.GetImage(latestFramePath, false);
                    SetPreviewImg(img);
                }
            }
            catch (Exception)
            {
                //Logger.Log("Error updating preview: " + e.Message, true);
            }
        }

        //public static async Task DeleteInterpolatedInputFrames()
        //{
        //    interpolatedInputFramesCount = 0;
        //    string[] inputFrames = IoUtils.GetFilesSorted(I.currentSettings.framesFolder);

        //    for (int i = 0; i < inputFrames.Length; i++)
        //    {
        //        while (Program.busy && (i + 10) > interpolatedInputFramesCount) await Task.Delay(1000);
        //        if (!Program.busy) break;

        //        if (i != 0 && i != inputFrames.Length - 1)
        //            IoUtils.OverwriteFileWithText(inputFrames[i]);

        //        if (i % 10 == 0) await Task.Delay(10);
        //    }
        //}

        public static Stopwatch timeSinceLastPreviewUpdate = new Stopwatch();

        public static void SetPreviewImg(Image img)
        {
            if (img == null)
                return;

            timeSinceLastPreviewUpdate.Restart();

            preview.Image = img;

            bigPreviewForm?.SetImage(img);
        }
    }
}
