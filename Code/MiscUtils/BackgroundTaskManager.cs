using System.Collections.Generic;

namespace Flowframes.MiscUtils
{
    class BackgroundTaskManager
    {
        private static ulong currentId = 0;
        private static readonly List<RunningTask> runningTasks = new List<RunningTask>();

        public class RunningTask
        {
            public NmkdStopwatch timer;
            public string name;
            public ulong id;
            public int timeoutSeconds;

            public RunningTask(string name, ulong id, int timeoutSeconds)
            {
                this.name = name;
                this.id = id;
                this.timeoutSeconds = timeoutSeconds;
                timer = new NmkdStopwatch();
            }
        }

        public static bool IsBusy()
        {
            Logger.Log($"[BgTaskMgr] BackgroundTaskManager is busy - {runningTasks.Count} tasks running.", true);
            return runningTasks.Count > 0;
        }

        public static void ClearExpired()
        {
            foreach (RunningTask task in runningTasks)
            {
                if (task.timer.Sw.ElapsedMilliseconds > task.timeoutSeconds * 1000)
                {
                    Logger.Log($"[BgTaskMgr] Task with ID {task.id} timed out, has been running for {task.timer}!", true);
                    runningTasks.Remove(task);
                }
            }
        }

        public static ulong Add(string name = "Unnamed Task", int timeoutSeconds = 120)
        {
            ulong id = currentId;
            runningTasks.Add(new RunningTask(name, currentId, timeoutSeconds));
            currentId++;
            return id;
        }

        public static void Remove(ulong id)
        {
            foreach (RunningTask task in new List<RunningTask>(runningTasks))
            {
                if (task.id == id)
                {
                    Logger.Log($"[BgTaskMgr] Task '{task.name}' has finished after {task.timer} (Timeout {task.timeoutSeconds}s)", true);
                    runningTasks.Remove(task);
                }
            }
        }
    }
}
