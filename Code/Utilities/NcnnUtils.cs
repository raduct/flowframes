using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.Os;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes.Utilities
{
    class NcnnUtils
    {
        public static int GetRifeNcnnGpuThreads(int gpuId, AI ai)
        {
            int threads = Config.GetInt(Config.Key.ncnnThreads);
            int maxThreads = VulkanUtils.GetMaxNcnnThreads(gpuId);

            threads = threads.Clamp(1, maxThreads); // To avoid exceeding the max queue count
            if (gpuId == -1)
            {
                maxThreads = Environment.ProcessorCount;
                threads = Environment.ProcessorCount - 4; // 4 threads are used by load & save
            }
            if (Interpolate.currentSettings.is3D)
                threads = threads / 2;
            Logger.Log($"Using {threads}/{maxThreads} compute threads on GPU with ID {gpuId}.", true, false, ai.LogFilename);

            return threads;
        }

        public static string GetNcnnPattern()
        {
            return $"%0{Padding.interpFrames}d{Interpolate.currentSettings.interpExt}";
        }

        public static string GetNcnnTilesize(int tilesize)
        {
            int gpusAmount = Config.Get(Config.Key.ncnnGpus).Split(',').Length;
            string tilesizeStr = $"{tilesize}";

            for (int i = 1; i < gpusAmount; i++)
                tilesizeStr += $",{tilesize}";

            return tilesizeStr;
        }

        public static string GetNcnnThreads(AI ai)
        {
            List<int> enabledGpuIds = Config.Get(Config.Key.ncnnGpus).Split(',').Select(s => s.GetInt()).ToList(); // Get GPU IDs
            List<int> gpuThreadCounts = enabledGpuIds.Select(g => GetRifeNcnnGpuThreads(g, ai)).ToList(); // Get max thread count for each GPU
            string progThreadsStr = string.Join(",", gpuThreadCounts);
            //return $"{(Interpolate.currentlyUsingAutoEnc ? 2 : 4)}:{progThreadsStr}:4"; // Read threads: 1 for singlethreaded, 2 for autoenc, 4 if order is irrelevant
            return Interpolate.currentSettings.is3D ? $"1:{progThreadsStr}:2" : $"1:{progThreadsStr}:3";
        }

        public static async Task DeleteNcnnDupes(string dir, float factor)
        {
            int dupeCount = InterpolateUtils.GetRoundedInterpFramesPerInputFrame(factor);
            var files = IoUtils.GetFileInfosSorted(dir, false).Reverse().Take(dupeCount).ToList();
            Logger.Log($"DeleteNcnnDupes: Calculated dupe count from factor; deleting last {dupeCount} interp frames of {IoUtils.GetAmountOfFiles(dir, false)} ({string.Join(", ", files.Select(x => x.Name))})", true);

            int attempts = 4;

            while (attempts > 0)
            {
                try
                {
                    files.ForEach(x => x.Delete());
                    break;
                }
                catch (Exception ex)
                {
                    attempts--;

                    if (attempts < 1)
                    {
                        Logger.Log($"DeleteNcnnDupes Error: {ex.Message}", true);
                        break;
                    }
                    else
                    {
                        await Task.Delay(500);
                    }
                }
            }
        }
    }
}
