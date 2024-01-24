using Flowframes.Extensions;
using Flowframes.Properties;
using System.Collections.Generic;
using System.Diagnostics;

namespace Flowframes.Os
{
    class AiProcessSuspend
    {
        public enum ProcessType { Main, Other, Both };

        public static bool aiProcFrozen;
        static List<Process> suspendedProcesses = new List<Process>();
        public static bool isRunning;

        public static void Reset()
        {
            SetRunning(false);
            SetPauseButtonStyle(false);
        }

        public static void SetRunning(bool running)
        {
            isRunning = running;
            Program.mainForm.GetPauseBtn().Visible = running;
        }

        public static void SuspendIfRunning()
        {
            if (!aiProcFrozen)
                SuspendResumeAi(true);
        }

        public static void ResumeIfPaused()
        {
            if (aiProcFrozen)
                SuspendResumeAi(false);
        }

        public static void SuspendResumeAi(bool freeze, ProcessType type = ProcessType.Both, bool excludeCmd = false)
        {
            if ((type == ProcessType.Both || type == ProcessType.Main) && AiProcess.lastAiProcess != null)
                Logger.Log($"{(freeze ? "Suspending" : "Resuming")} main process ({AiProcess.lastAiProcess.StartInfo.FileName} {AiProcess.lastAiProcess.StartInfo.Arguments})", true);
            if ((type == ProcessType.Both || type == ProcessType.Other) && AiProcess.lastAiProcessOther != null)
                Logger.Log($"{(freeze ? "Suspending" : "Resuming")} other process ({AiProcess.lastAiProcessOther.StartInfo.FileName} {AiProcess.lastAiProcessOther.StartInfo.Arguments})", true);

            if (freeze)
            {
                List<Process> procs = new List<Process>();

                if ((type == ProcessType.Both || type == ProcessType.Main) && AiProcess.lastAiProcess != null && !AiProcess.lastAiProcess.HasExited && !IsMainSuspended())
                {
                    procs.Add(AiProcess.lastAiProcess);

                    foreach (var subProc in OsUtils.GetChildProcesses(AiProcess.lastAiProcess))
                        procs.Add(subProc);
                }

                if ((type == ProcessType.Both || type == ProcessType.Other) && AiProcess.lastAiProcessOther != null && !AiProcess.lastAiProcessOther.HasExited && !IsOtherSuspended())
                {
                    procs.Add(AiProcess.lastAiProcessOther);

                    foreach (var subProc in OsUtils.GetChildProcesses(AiProcess.lastAiProcessOther))
                        procs.Add(subProc);
                }

                foreach (Process process in procs)
                {
                    if (process == null || process.HasExited)
                        continue;

                    if (excludeCmd && (process.ProcessName == "conhost" || process.ProcessName == "cmd"))
                        continue;

                    Logger.Log($"Suspending {process.ProcessName}", true);

                    process.Suspend();
                    suspendedProcesses.Add(process);
                }

                if ((AiProcess.lastAiProcess == null || AiProcess.lastAiProcess.HasExited || IsMainSuspended()) && (AiProcess.lastAiProcessOther == null || AiProcess.lastAiProcessOther.HasExited || IsOtherSuspended()))
                {
                    aiProcFrozen = true;
                    SetPauseButtonStyle(true);
                    AiProcess.processTime.Stop();
                }
            }
            else
            {
                aiProcFrozen = false;
                SetPauseButtonStyle(false);
                AiProcess.processTime.Start();

                foreach (Process process in new List<Process>(suspendedProcesses))   // We MUST clone the list here since we modify it in the loop!
                {
                    if (process == null || process.HasExited)
                        continue;

                    Logger.Log($"Resuming {process.ProcessName}", true);

                    process.Resume();
                    suspendedProcesses.Remove(process);
                }
            }
        }

        public static bool IsMainSuspended()
        {
            return suspendedProcesses.Contains(AiProcess.lastAiProcess);
        }

        public static bool IsOtherSuspended()
        {
            return suspendedProcesses.Contains(AiProcess.lastAiProcessOther);
        }

        public static void SetPauseButtonStyle(bool paused)
        {
            System.Windows.Forms.Button btn = Program.mainForm.GetPauseBtn();

            if (paused)
            {
                btn.BackgroundImage = Resources.baseline_play_arrow_white_48dp;
                btn.FlatAppearance.BorderColor = System.Drawing.Color.MediumSeaGreen;
                btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.MediumSeaGreen;
            }
            else
            {
                btn.BackgroundImage = Resources.baseline_pause_white_48dp;
                btn.FlatAppearance.BorderColor = System.Drawing.Color.DarkOrange;
                btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.DarkOrange;
            }
        }
    }
}
