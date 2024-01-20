﻿using Flowframes.Extensions;
using Flowframes.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes.Os
{
    class AiProcessSuspend
    {
        public static bool aiProcFrozen;
        static List<Process> suspendedProcesses = new List<Process>();
        public static bool isRunning;

        public static void Reset()
        {
            SetRunning(false);
            SetPauseButtonStyle(false);
        }

        public static void SetRunning (bool running)
        {
            isRunning = running;
            Program.mainForm.GetPauseBtn().Visible = running;
        }

        public static void SuspendIfRunning ()
        {
            if(!aiProcFrozen)
                SuspendResumeAi(true);
        }

        public static void ResumeIfPaused()
        {
            if (aiProcFrozen)
                SuspendResumeAi(false);
        }

        public static void SuspendResumeAi(bool freeze, bool excludeCmd = true)
        {
            Logger.Log($"{(freeze ? "Suspending" : "Resuming")} main process ({AiProcess.lastAiProcess.StartInfo.FileName} {AiProcess.lastAiProcess.StartInfo.Arguments})", true);
            if (Interpolate.currentSettings.is3D && AiProcess.lastAiProcessOther != null)
                Logger.Log($"{(freeze ? "Suspending" : "Resuming")} main process ({AiProcess.lastAiProcessOther.StartInfo.FileName} {AiProcess.lastAiProcessOther.StartInfo.Arguments})", true);

            if (freeze)
            {
                List<Process> procs = new List<Process>();
                Process currProcess = AiProcess.lastAiProcess;

                if (AiProcess.lastAiProcess != null && !AiProcess.lastAiProcess.HasExited)
                {
                    procs.Add(currProcess);

                    foreach (var subProc in OsUtils.GetChildProcesses(currProcess))
                        procs.Add(subProc);
                }

                if (Interpolate.currentSettings.is3D && AiProcess.lastAiProcessOther != null && !AiProcess.lastAiProcessOther.HasExited)
                {
                    currProcess = AiProcess.lastAiProcessOther;
                    procs.Add(currProcess);

                    foreach (var subProc in OsUtils.GetChildProcesses(currProcess))
                        procs.Add(subProc);
                }

                aiProcFrozen = true;
                SetPauseButtonStyle(true);
                AiProcess.processTime.Stop();

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

        public static void SetPauseButtonStyle (bool paused)
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
                btn.FlatAppearance.BorderColor= System.Drawing.Color.DarkOrange;
                btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.DarkOrange;
            }
        }
    }
}
