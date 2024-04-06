using Flowframes.Extensions;
using System.Collections.Generic;
using System.Diagnostics;

namespace Flowframes.Os
{
    class AiProcessSuspend
    {
        public enum ProcessType { Main, Other, Both };

        public static bool aiProcFrozen;
        static readonly List<Process> suspendedProcesses = new List<Process>();

        public static void Reset()
        {
            Program.mainForm.ShowPauseButton(false);
            Program.mainForm.SetPauseButtonStyle(false);
            aiProcFrozen = false;
            suspendedProcesses.Clear();
        }

        public static bool SuspendResume()
        {
            if (aiProcFrozen)
                Resume();
            else
                Suspend();
            return aiProcFrozen;
        }

        public static void Suspend(ProcessType type = ProcessType.Both, bool excludeCmd = false)
        {
            List<Process> procs = new List<Process>();

            if ((type == ProcessType.Both || type == ProcessType.Main) && IsMainRunning())
            {
                Logger.Log($"Suspending main process ({AiProcess.lastAiProcess.StartInfo.FileName} {AiProcess.lastAiProcess.StartInfo.Arguments})", true);

                procs.Add(AiProcess.lastAiProcess);
                foreach (var subProc in OsUtils.GetChildProcesses(AiProcess.lastAiProcess))
                    procs.Add(subProc);
            }

            if ((type == ProcessType.Both || type == ProcessType.Other) && IsOtherRunning())
            {
                Logger.Log($"Suspending other process ({AiProcess.lastAiProcessOther.StartInfo.FileName} {AiProcess.lastAiProcessOther.StartInfo.Arguments})", true);

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

            if (procs.Count > 0 && !IsMainRunning() && !IsOtherRunning())
            {
                aiProcFrozen = true;
                Program.mainForm.SetPauseButtonStyle(true);
                AiProcess.processTime.Stop();
            }
        }

        public static void Resume()
        {
            if (suspendedProcesses.Count == 0)
                return;

            if (AiProcess.lastAiProcess != null && !IsMainRunning())
                Logger.Log($"Resuming main process ({AiProcess.lastAiProcess.StartInfo.FileName} {AiProcess.lastAiProcess.StartInfo.Arguments})", true);
            if (AiProcess.lastAiProcessOther != null && !IsOtherRunning())
                Logger.Log($"Resuming other process ({AiProcess.lastAiProcessOther.StartInfo.FileName} {AiProcess.lastAiProcessOther.StartInfo.Arguments})", true);

            foreach (Process process in new List<Process>(suspendedProcesses))   // We MUST clone the list here since we modify it in the loop!
            {
                if (process != null && !process.HasExited)
                {
                    Logger.Log($"Resuming {process.ProcessName}", true);
                    process.Resume();
                }
                suspendedProcesses.Remove(process);
            }

            aiProcFrozen = false;
            Program.mainForm.SetPauseButtonStyle(false);
            AiProcess.processTime.Start();
        }

        static bool IsProcessRunning(Process process)
        {
            return process != null && !process.HasExited && !suspendedProcesses.Contains(process);
        }

        public static bool IsMainRunning()
        {
            return IsProcessRunning(AiProcess.lastAiProcess);
        }

        public static bool IsOtherRunning()
        {
            return IsProcessRunning(AiProcess.lastAiProcessOther);
        }
    }
}
