using Flowframes.Extensions;
using Flowframes.IO;
using Flowframes.Media;
using Flowframes.Os;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes
{
    class AvProcess
    {
        public static Process lastAvProcess;
        public enum LogMode { Visible, OnlyLastLine, Hidden }

        private const string defLogLevel = "warning";
        public static void Kill()
        {
            if (lastAvProcess == null) return;

            try
            {
                OsUtils.KillProcessTree(lastAvProcess.Id);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to kill lastAvProcess process tree: {e.Message}", true);
            }
        }

        public static async Task<string> RunFfmpeg(string args, LogMode logMode, string loglevel = null)
        {
            return await RunFfmpeg(args, null, logMode, loglevel);
        }

        public static async Task<string> RunFfmpeg(string args, string workingDir, LogMode logMode, string loglevel = null, bool progressBar = false, bool returnOutput = false)
        {
            bool show = Config.GetInt(Config.Key.cmdDebugMode) > 0;
            string processOutput = returnOutput ? string.Empty : null;
            Process ffmpeg = OsUtils.NewProcess(!show);
            lastAvProcess = ffmpeg;

            if (string.IsNullOrWhiteSpace(loglevel))
                loglevel = defLogLevel;

            string beforeArgs = $"-hide_banner -stats -loglevel {loglevel} -y";

            if (!string.IsNullOrWhiteSpace(workingDir))
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {workingDir.Wrap()} & {Path.Combine(GetAvDir(), "ffmpeg.exe").Wrap()} {beforeArgs} {args}";
            else
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetAvDir().Wrap()} & ffmpeg {beforeArgs} {args}";

            if (logMode != LogMode.Hidden) Logger.Log("Running FFmpeg...", false);
            Logger.Log($"ffmpeg {beforeArgs} {args}", true, false, "ffmpeg");

            if (!show)
            {
                TaskCompletionSource<object> outputTcs = new TaskCompletionSource<object>(), errorTcs = new TaskCompletionSource<object>();
                ffmpeg.OutputDataReceived += (sender, outLine) => { if (outLine.Data == null) outputTcs.SetResult(null); else AvOutputHandler.LogOutput(outLine.Data, ref processOutput, "ffmpeg", logMode, progressBar); };
                ffmpeg.ErrorDataReceived += (sender, outLine) => { if (outLine.Data == null) errorTcs.SetResult(null); else AvOutputHandler.LogOutput(outLine.Data, ref processOutput, "ffmpeg", logMode, progressBar); };
                ffmpeg.Start();
                ffmpeg.PriorityClass = ProcessPriorityClass.BelowNormal;
                ffmpeg.BeginOutputReadLine();
                ffmpeg.BeginErrorReadLine();
                await Task.WhenAll(ffmpeg.WaitForExitAsync(), outputTcs.Task, errorTcs.Task);
            }
            else
            {
                ffmpeg.Start();
                ffmpeg.PriorityClass = ProcessPriorityClass.BelowNormal;
                await ffmpeg.WaitForExitAsync();
            }

            if (progressBar)
                Program.mainForm.SetProgress(0);

            return processOutput;
        }

        public static string RunFfmpegSync(string args, string workingDir = "", LogMode logMode = LogMode.Hidden, string loglevel = "")
        {
            Process ffmpeg = OsUtils.NewProcess(true);
            lastAvProcess = ffmpeg;

            if (string.IsNullOrWhiteSpace(loglevel))
                loglevel = defLogLevel;

            string beforeArgs = $"-hide_banner -stats -loglevel {loglevel} -y";

            if (!string.IsNullOrWhiteSpace(workingDir))
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {workingDir.Wrap()} & {Path.Combine(GetAvDir(), "ffmpeg.exe").Wrap()} {beforeArgs} {args}";
            else
                ffmpeg.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetAvDir().Wrap()} & ffmpeg {beforeArgs} {args}";

            if (logMode != LogMode.Hidden) Logger.Log("Running FFmpeg...", false);
            Logger.Log($"ffmpeg {beforeArgs} {args}", true, false, "ffmpeg");

            ffmpeg.StartInfo.Arguments += " 2>&1";
            ffmpeg.Start();
            ffmpeg.PriorityClass = ProcessPriorityClass.BelowNormal;
            string output = ffmpeg.StandardOutput.ReadToEnd();
            ffmpeg.WaitForExit();
            Logger.Log($"Synchronous ffmpeg output:\n{output}", true, false, "ffmpeg");
            return output;
        }

        public static string GetFfmpegDefaultArgs(string loglevel = "warning")
        {
            return $"-hide_banner -stats -loglevel {loglevel} -y";
        }

        public class FfprobeSettings
        {
            public string Args { get; set; } = "";
            public LogMode LoggingMode { get; set; } = LogMode.Hidden;
            public string LogLevel { get; set; } = "panic";
            public bool SetBusy { get; set; } = false;
        }

        public static async Task<string> RunFfprobe(FfprobeSettings settings, bool asyncOutput = false)
        {
            bool show = Config.GetInt(Config.Key.cmdDebugMode) > 0;

            Process ffprobe = OsUtils.NewProcess(!show);

            bool concat = settings.Args.Split(" \"").Last().Remove("\"").Trim().EndsWith(".concat");
            string args = $"-v {settings.LogLevel} {(concat ? "-f concat -safe 0 " : "")}{settings.Args}";
            ffprobe.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetAvDir().Wrap()} & ffprobe {args}";

            if (settings.LoggingMode != LogMode.Hidden) Logger.Log("Running FFprobe...", false);
            Logger.Log($"ffprobe {args}", true, false, "ffmpeg");

            if (!asyncOutput)
                return await Task.Run(() => OsUtils.GetProcStdOut(ffprobe));

            string processOutput = string.Empty;
            if (!show)
            {
                TaskCompletionSource<object> outputTcs = new TaskCompletionSource<object>(), errorTcs = new TaskCompletionSource<object>();
                ffprobe.OutputDataReceived += (sender, outLine) => { if (outLine.Data == null) outputTcs.SetResult(null); else processOutput += outLine + "\n"; };
                ffprobe.ErrorDataReceived += (sender, outLine) => { if (outLine.Data == null) errorTcs.SetResult(null); else processOutput += outLine + "\n"; };
                ffprobe.Start();
                ffprobe.PriorityClass = ProcessPriorityClass.BelowNormal;
                ffprobe.BeginOutputReadLine();
                ffprobe.BeginErrorReadLine();
                await Task.WhenAll(ffprobe.WaitForExitAsync(), outputTcs.Task, errorTcs.Task);
            }
            else
            {
                ffprobe.Start();
                ffprobe.PriorityClass = ProcessPriorityClass.BelowNormal;
                await ffprobe.WaitForExitAsync();
            }

            return processOutput;
        }

        public static string GetFfprobeOutput(string args)
        {
            Process ffprobe = OsUtils.NewProcess(true);
            ffprobe.StartInfo.Arguments = $"{GetCmdArg()} cd /D {GetAvDir().Wrap()} & ffprobe.exe {args}";
            Logger.Log($"ffprobe {args}", true, false, "ffmpeg");
            ffprobe.Start();
            ffprobe.WaitForExit();
            string output = ffprobe.StandardOutput.ReadToEnd();
            string err = ffprobe.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err)) output += "\n" + err;
            return output;
        }

        static string GetAvDir()
        {
            return Path.Combine(Paths.GetPkgPath(), Paths.audioVideoDir);
        }

        static string GetCmdArg()
        {
            return "/C";
        }

        //public static async Task SetBusyWhileRunning()
        //{
        //    if (Program.busy) return;

        //    await Task.Delay(100);
        //    while (!lastAvProcess.HasExited)
        //        await Task.Delay(10);
        //}
    }
}
