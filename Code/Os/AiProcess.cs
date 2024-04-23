using Flowframes.Data;
using Flowframes.Extensions;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.MiscUtils;
using Flowframes.Ui;
using Flowframes.Utilities;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Paths = Flowframes.IO.Paths;

namespace Flowframes.Os
{
    class AiProcess
    {
        public static bool hasShownError;
        static string lastLogName;
        public static Process lastAiProcess;
        public static Process lastAiProcessOther;
        public static Stopwatch processTime = new Stopwatch();

        public static int lastStartupTimeMs = 1000;

        public static void Kill()
        {
            try
            {
                Program.mainForm.ShowPauseButton(false);
                if (lastAiProcess != null && !lastAiProcess.HasExited)
                    OsUtils.KillProcessTree(lastAiProcess.Id);
                if (lastAiProcessOther != null && !lastAiProcessOther.HasExited)
                    OsUtils.KillProcessTree(lastAiProcessOther.Id);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to kill currentAiProcess process tree: {e.Message}", true);
            }
        }

        static void AiStarted(Process proc, int startupTimeMs)
        {
            lastStartupTimeMs = startupTimeMs;
            processTime.Restart();
            lastAiProcess = proc;
            Program.mainForm.ShowPauseButton(true);
            hasShownError = false;
        }

        static void AiStartedRt(Process proc)
        {
            lastAiProcess = proc;
            Program.mainForm.ShowPauseButton(true);
            hasShownError = false;
        }

        static void SetProgressCheck(string framesPath, string interpPath, float factor)
        {
            int frames = IoUtils.GetAmountOfFiles(framesPath, false);
            int target = ((frames * factor) - (factor - 1)).RoundToInt();
            InterpolationProgress.currentFactor = factor;

            if (InterpolationProgress.progCheckRunning)
                InterpolationProgress.targetFrames = target;
            else
                InterpolationProgress.GetProgressByFrameAmount(interpPath, target);
        }

        static void SetProgressCheck(int sourceFrames, float factor, string logFile)
        {
            int target = ((sourceFrames * factor) - (factor - 1)).RoundToInt();
            InterpolationProgress.currentFactor = factor;

            if (InterpolationProgress.progCheckRunning)
                InterpolationProgress.targetFrames = target;
            else
                InterpolationProgress.GetProgressFromFfmpegLog(logFile, target);
        }

        static async Task AiFinished(string aiName, bool rt = false)
        {
            if (Interpolate.canceled) return;
            Program.mainForm.SetProgress(100);
            Program.mainForm.ShowPauseButton(false);

            if (rt)
            {
                Logger.Log($"Stopped running {aiName}.");
                return;
            }

            int interpFramesFiles = IoUtils.GetAmountOfFiles(Interpolate.currentSettings.interpFolder, false, "*" + Interpolate.currentSettings.interpExt);
            int interpFramesCount = interpFramesFiles + InterpolationProgress.deletedFramesCount;

            if (!Interpolate.currentSettings.ai.Piped)
                InterpolationProgress.UpdateInterpProgress(interpFramesCount, InterpolationProgress.targetFrames);

            processTime.Stop();
            string logStr = $"Done running {aiName} - Interpolation took {FormatUtils.Time(processTime.Elapsed)}. Peak Output FPS: {InterpolationProgress.peakFpsOut:0.00}";

            if (Interpolate.currentlyUsingAutoEnc && AutoEncode.HasWorkToDo())
            {
                logStr += " - Waiting for encoding to finish...";
                Program.mainForm.SetStatus("Creating output video from frames...");
            }

            if (Interpolate.currentSettings.outSettings.Format != Enums.Output.Format.Realtime)
                Logger.Log(logStr);

            if (!Interpolate.currentSettings.ai.Piped && interpFramesCount < 3)
            {
                string amount = interpFramesCount > 0 ? $"Only {interpFramesCount}" : "No";

                if (lastLogName.IsEmpty())
                {
                    Interpolate.Cancel($"Interpolation failed - {amount} interpolated frames were created, and no log was written.");
                    return;
                }

                string log = string.Join("\n", Logger.GetLogLastLines(lastLogName, 10).Select(x => x.Split("]: ").Last()));
                Interpolate.Cancel($"Interpolation failed - {amount} interpolated frames were created.\n\n\nLast 10 log lines:\n{log}\n\nCheck the log '{lastLogName}' for more details.");
                return;
            }

            await Task.CompletedTask;
        }

        private static async Task RunAIProcessAsynch(bool logOutput, Process process, AI ai, bool main = true)
        {
            if (logOutput)
            {
                TaskCompletionSource<object> outputTcs = new TaskCompletionSource<object>(), errorTcs = new TaskCompletionSource<object>();
                process.OutputDataReceived += (sender, outLine) =>
                    {
                        if (outLine.Data == null)
                            outputTcs.SetResult(null);
                        else
                            LogOutput(outLine.Data, ai, false, main);
                    };
                process.ErrorDataReceived += (sender, outLine) =>
                    {
                        if (outLine.Data == null)
                            errorTcs.SetResult(null);
                        else
                            LogOutput("[E] " + outLine.Data, ai, true, main);
                    };
                process.Start();
                //process.PriorityClass = ProcessPriorityClass.AboveNormal; // cmd.exe not inhereted for above normal
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.WhenAll(process.WaitForExitAsync(), outputTcs.Task, errorTcs.Task);
            }
            else
            {
                process.Start();
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
                await process.WaitForExitAsync();
            }
        }

        public static async Task RunRifeCuda(string framesPath, float interpFactor, string mdl)
        {
            AI ai = Implementations.rifeCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string rifeDir = Path.Combine(Paths.GetPkgPath(), ai.PkgDir);
                string script = "rife.py";

                if (!File.Exists(Path.Combine(rifeDir, script)))
                {
                    Interpolate.Cancel("RIFE script not found! Make sure you didn't modify any files.");
                    return;
                }

                string archFilesDir = Path.Combine(rifeDir, "arch");
                string archFilesDirModel = Path.Combine(rifeDir, mdl, "arch");

                if (Directory.Exists(archFilesDirModel))
                {
                    Logger.Log($"Model {mdl} has architecture python files - copying to arch.", true);
                    IoUtils.DeleteContentsOfDir(archFilesDir);
                    IoUtils.CopyDir(archFilesDirModel, archFilesDir);
                }

                await RunRifeCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunRifeCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdl)
        {
            string outPath = Path.Combine(Interpolate.currentSettings.tempFolder, outDir);
            Directory.CreateDirectory(outPath);
            string uhdStr = await InterpolateUtils.UseUhd() ? "--UHD" : "";
            string wthreads = $"--wthreads {2 * (int)interpFactor}";
            string rbuffer = $"--rbuffer {Config.GetInt(Config.Key.rifeCudaBufferSize, 200)}";
            //string scale = $"--scale {Config.GetFloat("rifeCudaScale", 1.0f).ToStringDot()}";
            string prec = Config.GetBool(Config.Key.rifeCudaFp16) ? "--fp16" : "";
            string args = $" --input {inPath.Wrap()} --output {outDir} --model {mdl} --multi {interpFactor} {uhdStr} {wthreads} {rbuffer} {prec}";

            Process rifePy = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            AiStarted(rifePy, 3500);
            SetProgressCheck(inPath, outPath, interpFactor);
            rifePy.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.rifeCuda.PkgDir).Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Running RIFE (CUDA){(await InterpolateUtils.UseUhd() ? " (UHD Mode)" : "")}...", false);
            Logger.Log("cmd.exe " + rifePy.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), rifePy, Implementations.rifeCuda);
        }

        public static async Task RunFlavrCuda(string framesPath, float interpFactor, string mdl)
        {
            AI ai = Implementations.flavrCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string flavDir = Path.Combine(Paths.GetPkgPath(), ai.PkgDir);
                string script = "flavr.py";

                if (!File.Exists(Path.Combine(flavDir, script)))
                {
                    Interpolate.Cancel("FLAVR script not found! Make sure you didn't modify any files.");
                    return;
                }

                await RunFlavrCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunFlavrCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdl)
        {
            string outPath = Path.Combine(Interpolate.currentSettings.tempFolder, outDir);
            Directory.CreateDirectory(outPath);
            string args = $" --input {inPath.Wrap()} --output {outPath.Wrap()} --model {mdl}/{mdl}.pth --factor {interpFactor}";

            Process flavrPy = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            AiStarted(flavrPy, 4000);
            SetProgressCheck(inPath, outPath, interpFactor);
            flavrPy.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.flavrCuda.PkgDir).Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Running FLAVR (CUDA)...", false);
            Logger.Log("cmd.exe " + flavrPy.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), flavrPy, Implementations.flavrCuda);
        }

        public static async Task RunRifeNcnn(string framesPath, string outPath, float factor, string mdl)
        {
            AI ai = Implementations.rifeNcnn;
            try
            {
                Logger.Log($"Running RIFE (NCNN){(await InterpolateUtils.UseUhd() ? " (UHD Mode)" : "")}...", false);

                Task otherProc = null;
                if (Interpolate.currentSettings.is3D)
                    otherProc = Task.Run(async () =>
                    {
                        await RunRifeNcnnProcess(Paths.GetOtherDir(framesPath), factor, Paths.GetOtherDir(outPath), mdl, false);
                        await NcnnUtils.DeleteNcnnDupes(Paths.GetOtherDir(outPath), factor);
                    });

                await RunRifeNcnnProcess(framesPath, factor, outPath, mdl);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);

                if (otherProc != null)
                    await otherProc;
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunRifeNcnnProcess(string inPath, float factor, string outPath, string mdl, bool main = true)
        {
            Directory.CreateDirectory(outPath);
            Process rifeNcnn = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            int targetFrames = (IoUtils.GetAmountOfFiles(inPath, false, "*.*") * factor).RoundToInt(); // TODO: Maybe won't work with fractional factors ??

            string frames = mdl.Contains("v4") ? $"-n {targetFrames}" : "";
            string uhdStr = await InterpolateUtils.UseUhd() ? "-u" : "";
            string ttaStr = Config.GetBool(Config.Key.rifeNcnnUseTta, false) ? "-x" : "";

            rifeNcnn.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.rifeNcnn.PkgDir).Wrap()} & start /b /abovenormal /wait rife-ncnn-vulkan.exe" +
                $" -v -i {inPath.Wrap()} -o {outPath.Wrap()} {frames} -m {mdl.ToLowerInvariant()} {ttaStr} {uhdStr} -g {Config.Get(Config.Key.ncnnGpus)} -f {NcnnUtils.GetNcnnPattern()} -j {NcnnUtils.GetNcnnThreads(Implementations.rifeNcnn)}";

            Logger.Log("cmd.exe " + rifeNcnn.StartInfo.Arguments, true);

            if (main)
            {
                AiStarted(rifeNcnn, 1000);
                SetProgressCheck(inPath, outPath, factor);
            }
            else
                lastAiProcessOther = rifeNcnn;

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), rifeNcnn, Implementations.rifeNcnn, main);
        }

        public static async Task RunRifeNcnnVs(string framesPath, string outPath, float factor, string mdl, bool rt = false)
        {
            if (Interpolate.canceled) return;

            AI ai = Implementations.rifeNcnnVs;

            try
            {
                Size scaledSize = await InterpolateUtils.GetOutputResolution(Interpolate.currentSettings.inPath, false, false);
                Logger.Log($"Running RIFE (NCNN-VS){(InterpolateUtils.UseUhd(scaledSize) ? " (UHD Mode)" : "")}...", false);

                await RunRifeNcnnVsProcess(framesPath, factor, outPath, mdl, scaledSize, rt);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunRifeNcnnVsProcess(string inPath, float factor, string outPath, string mdl, Size res, bool rt = false)
        {
            IoUtils.CreateDir(outPath);
            Process rifeNcnnVs = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            string avDir = Path.Combine(Paths.GetPkgPath(), Paths.audioVideoDir);
            string pipedTargetArgs = $"{Path.Combine(avDir, "ffmpeg").Wrap()} -y {await Export.GetPipedFfmpegCmd(rt)}";
            string pkgDir = Path.Combine(Paths.GetPkgPath(), Implementations.rifeNcnnVs.PkgDir);
            int gpuId = Config.Get(Config.Key.ncnnGpus).Split(',')[0].GetInt();

            VapourSynthUtils.VsSettings vsSettings = new VapourSynthUtils.VsSettings()
            {
                InterpSettings = Interpolate.currentSettings,
                ModelDir = mdl,
                Factor = factor,
                Res = res,
                Uhd = InterpolateUtils.UseUhd(res),
                GpuId = gpuId,
                GpuThreads = NcnnUtils.GetRifeNcnnGpuThreads(gpuId, Implementations.rifeNcnnVs),
                SceneDetectSensitivity = Config.GetBool(Config.Key.scnDetect) ? Config.GetFloat(Config.Key.scnDetectValue) * 0.7f : 0f,
                Loop = Config.GetBool(Config.Key.enableLoop),
                MatchDuration = Config.GetBool(Config.Key.fixOutputDuration),
                Dedupe = Config.GetInt(Config.Key.dedupMode) != 0,
                Realtime = rt,
                Osd = Config.GetBool(Config.Key.vsRtShowOsd),
            };

            if (rt)
            {
                Logger.Log($"Starting. Use Space to pause, Left Arrow and Right Arrow to seek, though seeking can be slow.");
                AiStartedRt(rifeNcnnVs);
            }
            else
            {
                SetProgressCheck(Interpolate.currentMediaFile.FrameCount, factor, Implementations.rifeNcnnVs.LogFilename);
                AiStarted(rifeNcnnVs, 1000);
            }

            rifeNcnnVs.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {pkgDir.Wrap()} & vspipe {VapourSynthUtils.CreateScript(vsSettings).Wrap()} -c y4m - | {pipedTargetArgs}";

            Logger.Log("cmd.exe " + rifeNcnnVs.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), rifeNcnnVs, Implementations.rifeNcnnVs);
        }

        public static async Task RunDainNcnn(string framesPath, string outPath, float factor, string mdl, int tilesize)
        {
            AI ai = Implementations.dainNcnn;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                await RunDainNcnnProcess(framesPath, outPath, factor, mdl, tilesize);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunDainNcnnProcess(string framesPath, string outPath, float factor, string mdl, int tilesize)
        {
            string dainDir = Path.Combine(Paths.GetPkgPath(), Implementations.dainNcnn.PkgDir);
            Directory.CreateDirectory(outPath);
            Process dain = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            AiStarted(dain, 1500);
            SetProgressCheck(framesPath, outPath, factor);
            int targetFrames = (IoUtils.GetAmountOfFiles(framesPath, false, "*.*") * factor).RoundToInt();

            string args = $" -v -i {framesPath.Wrap()} -o {outPath.Wrap()} -n {targetFrames} -m {mdl.ToLowerInvariant()}" +
                $" -t {NcnnUtils.GetNcnnTilesize(tilesize)} -g {Config.Get(Config.Key.ncnnGpus)} -f {NcnnUtils.GetNcnnPattern()} -j 2:1:2";

            dain.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {dainDir.Wrap()} & dain-ncnn-vulkan.exe {args}";
            Logger.Log("Running DAIN...", false);
            Logger.Log("cmd.exe " + dain.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), dain, Implementations.dainNcnn);
        }

        public static async Task RunXvfiCuda(string framesPath, float interpFactor, string mdl)
        {
            AI ai = Implementations.xvfiCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string xvfiDir = Path.Combine(Paths.GetPkgPath(), Implementations.xvfiCuda.PkgDir);
                string script = "main.py";

                if (!File.Exists(Path.Combine(xvfiDir, script)))
                {
                    Interpolate.Cancel("XVFI script not found! Make sure you didn't modify any files.");
                    return;
                }

                await RunXvfiCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunXvfiCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdlDir)
        {
            string pkgPath = Path.Combine(Paths.GetPkgPath(), Implementations.xvfiCuda.PkgDir);
            string basePath = Interpolate.currentSettings.tempFolder;
            string outPath = Path.Combine(basePath, outDir);
            Directory.CreateDirectory(outPath);
            string mdlArgs = File.ReadAllText(Path.Combine(pkgPath, mdlDir, "args.ini"));
            string args = $" --custom_path {basePath.Wrap()} --input {inPath.Wrap()} --output {outPath.Wrap()} --mdl_dir {mdlDir}" +
                $" --multiple {interpFactor} --gpu 0 {mdlArgs}";

            Process xvfiPy = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            AiStarted(xvfiPy, 3500);
            SetProgressCheck(inPath, outPath, interpFactor);
            xvfiPy.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {pkgPath.Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Running XVFI (CUDA)...", false);
            Logger.Log("cmd.exe " + xvfiPy.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), xvfiPy, Implementations.xvfiCuda);
        }

        public static async Task RunIfrnetNcnn(string framesPath, string outPath, float factor, string mdl)
        {
            AI ai = Implementations.ifrnetNcnn;

            try
            {
                Logger.Log($"Running IFRNet (NCNN){(await InterpolateUtils.UseUhd() ? " (UHD Mode)" : "")}...", false);

                await RunIfrnetNcnnProcess(framesPath, factor, outPath, mdl);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);
            }
            catch (Exception e)
            {
                Logger.Log($"Error running {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunIfrnetNcnnProcess(string inPath, float factor, string outPath, string mdl)
        {
            Directory.CreateDirectory(outPath);
            Process ifrnetNcnn = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd());
            AiStarted(ifrnetNcnn, 1500);
            SetProgressCheck(inPath, outPath, factor);
            //int targetFrames = ((IoUtils.GetAmountOfFiles(lastInPath, false, "*.*") * factor).RoundToInt()); // TODO: Maybe won't work with fractional factors ??
            //string frames = mdl.Contains("v4") ? $"-n {targetFrames}" : "";
            string uhdStr = ""; // await InterpolateUtils.UseUhd() ? "-u" : "";
            string ttaStr = ""; // Config.GetBool(Config.Key.rifeNcnnUseTta, false) ? "-x" : "";

            ifrnetNcnn.StartInfo.Arguments = $"{OsUtils.GetCmdArg()} cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.ifrnetNcnn.PkgDir).Wrap()} & ifrnet-ncnn-vulkan.exe " +
                $" -v -i {inPath.Wrap()} -o {outPath.Wrap()} -m {mdl} {ttaStr} {uhdStr} -g {Config.Get(Config.Key.ncnnGpus)} -f {NcnnUtils.GetNcnnPattern()} -j {NcnnUtils.GetNcnnThreads(Implementations.ifrnetNcnn)}";

            Logger.Log("cmd.exe " + ifrnetNcnn.StartInfo.Arguments, true);

            await RunAIProcessAsynch(!OsUtils.ShowHiddenCmd(), ifrnetNcnn, Implementations.ifrnetNcnn);
        }

        static void LogOutput(string line, AI ai, bool err, bool main)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 6)
                return;

            lastLogName = ai.LogFilename;
            if (!line.EndsWith(" done"))
                Logger.Log(line, true, false, ai.LogFilename);

            if (InterpolationProgress.UpdateLastFrameFromInterpOutput(line, main))
                return;

            // Check for errors
            string errorMsg = null;
            if (ai.Backend == AI.AiBackend.Pytorch) // Pytorch specific
            {
                if (line.Contains("ff:nocuda-cpu"))
                    Logger.Log("WARNING: CUDA-capable GPU device is not available, running on CPU instead!");

                if (!hasShownError && err && line.Contains("modulenotfounderror", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    errorMsg = $"A python module is missing.\nCheck {ai.LogFilename} for details.\n\n{line}";
                }

                if (!hasShownError && line.Contains("no longer supports this gpu", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    errorMsg = $"Your GPU seems to be outdated and is not supported!\n\n{line}";
                }

                if (!hasShownError && line.Contains("error(s) in loading state_dict", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    string msg = (Interpolate.currentSettings.ai.NameInternal == Implementations.flavrCuda.NameInternal) ? "\n\nFor FLAVR, you need to select the correct model for each scale!" : "";
                    errorMsg = $"Error loading the AI model!\n\n{line}{msg}";
                }

                if (!hasShownError && line.Contains("unicodeencodeerror", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    errorMsg = $"It looks like your path contains invalid characters - remove them and try again!\n\n{line}";
                }

                if (!hasShownError && err && (line.Contains("RuntimeError") || line.Contains("ImportError") || line.Contains("OSError")))
                {
                    hasShownError = true;
                    string lastLogLines = string.Join("\n", Logger.GetLogLastLines(lastLogName, 6).Select(x => $"[{x.Split("]: [").Skip(1).FirstOrDefault()}"));
                    errorMsg = $"A python error occured during interpolation!\nCheck the log for details:\n\n{lastLogLines}";
                }
            }

            if (ai.Backend == AI.AiBackend.Ncnn) // NCNN specific
            {
                if (!hasShownError && err && line.MatchesWildcard("vk*Instance* failed"))
                {
                    hasShownError = true;
                    errorMsg = $"Vulkan failed to start up!\n\n{line}\n\nThis most likely means your GPU is not compatible.";
                }

                if (!hasShownError && err && line.Contains("vkAllocateMemory failed"))
                {
                    hasShownError = true;
                    bool usingDain = (Interpolate.currentSettings.ai.NameInternal == Implementations.dainNcnn.NameInternal);
                    string msg = usingDain ? "\n\nTry reducing the tile size in the AI settings." : "\n\nTry a lower resolution (Settings -> Max Video Size).";
                    errorMsg = $"Vulkan ran out of memory!\n\n{line}{msg}";
                }

                if (!hasShownError && err && line.Contains("invalid gpu device"))
                {
                    hasShownError = true;
                    errorMsg = $"A Vulkan error occured during interpolation!\n\n{line}\n\nAre your GPU IDs set correctly?";
                }

                if (!hasShownError && err && line.MatchesWildcard("vk* failed"))
                {
                    hasShownError = true;
                    string lastLogLines = string.Join("\n", Logger.GetLogLastLines(lastLogName, 6).Select(x => $"[{x.Split("]: [").Skip(1).FirstOrDefault()}"));
                    errorMsg = $"A Vulkan error occured during interpolation!\n\n{lastLogLines}";
                }
            }

            if (ai.Piped) // VS specific
            {
                if (!hasShownError && Interpolate.currentSettings.outSettings.Format != Enums.Output.Format.Realtime && line.Contains("fwrite() call failed", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    string lastLogLines = string.Join("\n", Logger.GetLogLastLines(lastLogName, 6).Select(x => $"[{x.Split("]: [").Skip(1).FirstOrDefault()}"));
                    errorMsg = $"VapourSynth interpolation failed with an unknown error. Check the log for details:\n\n{lastLogLines}";
                }

                if (!hasShownError && line.Contains("allocate memory failed", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    errorMsg = $"Out of memory!\nTry reducing your RAM usage by closing some programs.\n\n{line}";
                }

                if (!hasShownError && line.Contains("vapoursynth.error:", StringComparison.InvariantCultureIgnoreCase))
                {
                    hasShownError = true;
                    errorMsg = $"VapourSynth Error:\n\n{line}";
                }
            }

            if (!hasShownError && err && line.Contains("out of memory", StringComparison.InvariantCultureIgnoreCase))
            {
                hasShownError = true;
                errorMsg = $"Your GPU ran out of VRAM! Please try a video with a lower resolution or use the Max Video Size option in the settings.\n\n{line}";
            }

            if (!hasShownError && line.Contains("illegal memory access", StringComparison.InvariantCultureIgnoreCase))
            {
                hasShownError = true;
                errorMsg = $"Your GPU appears to be unstable! If you have an overclock enabled, please disable it!\n\n{line}";
            }

            // First cancel then show message that waits for user input
            if (hasShownError)
                Interpolate.Cancel(errorMsg);
        }

        public static bool IsRunning()
        {
            return (lastAiProcess != null && !lastAiProcess.HasExited) || (lastAiProcessOther != null && !lastAiProcessOther.HasExited);
        }
    }
}
