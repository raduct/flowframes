using Flowframes.Forms;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.MiscUtils;
using Flowframes.Os;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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

        static Regex frameRegex;

        public static void Restart()
        {
            progCheckRunning = true;
            deletedFramesCount = 0;
            lastFrame = lastOtherFrame = 0;
            peakFpsOut = 0f;
            Program.mainForm.SetProgress(0);

            string ncnnStr = I.currentSettings.ai.NameInternal.Contains("NCNN") ? " done" : "";
            frameRegex = new Regex($@"\d*(?={I.currentSettings.interpExt}{ncnnStr})");
        }

        public static async void GetProgressByFrameAmount(string outdir, int target)
        {
            targetFrames = target;
            Restart();
            Logger.Log($"Starting GetProgressByFrameAmount() loop for outdir '{outdir}', target is {target} frames", true);
            bool firstProgUpd = true;

            while (Program.busy)
            {
                if (Directory.Exists(outdir))
                {
                    if (firstProgUpd && Program.mainForm.IsInFocus())
                        Program.mainForm.SetTab(Program.mainForm.previewTab.Name);

                    firstProgUpd = false;
                    int lastFrameNo = I.currentSettings.is3D ? Math.Min(lastFrame, lastOtherFrame) : lastFrame;

                    if (lastFrameNo > 1)
                        UpdateInterpProgress(lastFrameNo, targetFrames, outdir);

                    await Task.Delay(target < 1000 ? 200 : 1000);

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

        public static bool UpdateLastFrameFromInterpOutput(string output, bool main)
        {
            try
            {
                Match result = frameRegex.Match(output);
                if (result.Success)
                {
                    int frame = int.Parse(result.Value);
                    if (!main)
                        lastOtherFrame = Math.Max(frame, lastOtherFrame);
                    else
                        lastFrame = Math.Max(frame, lastFrame);
                    return true;
                }
            }
            catch
            {
                Logger.Log($"UpdateLastFrameFromInterpOutput: Failed to get progress from '{output}' even though Regex matched!", true);
            }
            return false;
        }

        public static async void GetProgressFromFfmpegLog(string logFile, int target)
        {
            targetFrames = target;
            Restart();
            Logger.Log($"Starting GetProgressFromFfmpegLog() loop for log '{logFile}', target is {target} frames", true);
            UpdateInterpProgress(0, targetFrames);

            while (Program.busy)
            {
                string lastLogLine = Logger.GetLogLastLine(logFile);
                int num = lastLogLine == null ? 0 : lastLogLine.Split("frame=")[1].Split("fps=")[0].GetInt();

                if (num > 0)
                    UpdateInterpProgress(num, targetFrames);

                await Task.Delay(500);

                if (num >= targetFrames)
                    break;
            }

            progCheckRunning = false;

            if (I.canceled)
                Program.mainForm.SetProgress(0);
        }

        public static float peakFpsOut;

        private const int previewUpdateRateMs = 200;

        public static void UpdateInterpProgress(int frames, int target, string currentOutdir = "")
        {
            if (I.canceled) return;
            target = (target / I.InterpProgressMultiplier).RoundToInt();
            frames = frames.Clamp(0, target);
            int percent = FormatUtils.RatioInt(frames, target);
            Program.mainForm.SetProgress(percent);

            float generousTime = (AiProcess.processTime.ElapsedMilliseconds - AiProcess.lastStartupTimeMs) / 1000f;
            float fps = (frames / generousTime).Clamp(0, 9999);
            string fpsIn = (fps / currentFactor).ToString("0.00");
            string fpsOut = fps.ToString("0.00");

            if (fps > peakFpsOut)
                peakFpsOut = fps;

            float secondsPerFrame = generousTime / frames;
            int framesLeft = target - frames;
            float eta = framesLeft * secondsPerFrame;
            string etaStr = FormatUtils.Time(new TimeSpan(0, 0, eta.RoundToInt()), false);

            bool replaceLine = Logger.LastUiLine.Contains("Average Speed: ");

            string logStr = $"Interpolated {frames}/{target} Frames ({percent}%) - Average Speed: {fpsIn} FPS In / {fpsOut} FPS Out - ";
            logStr += $"Time: {FormatUtils.Time(AiProcess.processTime.Elapsed)} - ETA: {etaStr}";
            if (AutoEncode.encoding) logStr += " - Encoding...";
            Logger.Log(logStr, false, replaceLine);
            try
            {
                if (!string.IsNullOrWhiteSpace(currentOutdir) && frames > currentFactor)
                {
                    if (bigPreviewForm == null && !preview.Visible  /* ||Program.mainForm.WindowState != FormWindowState.Minimized */ /* || !Program.mainForm.IsInFocus()*/) return;        // Skip if the preview is not visible or the form is not in focus
                    if (timeSinceLastPreviewUpdate.IsRunning && timeSinceLastPreviewUpdate.ElapsedMilliseconds < previewUpdateRateMs) return;
                    string lastFramePath = Path.Combine(currentOutdir, frames.ToString().PadLeft(Data.Padding.interpFrames, '0') + I.currentSettings.interpExt);
                    Image img = IoUtils.GetImage(lastFramePath, false);
                    SetPreviewImg(img);
                }
            }
            catch (Exception)
            {
                //Logger.Log("Error updating preview: " + e.Message, true);
            }
        }

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
