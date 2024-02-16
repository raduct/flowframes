using Flowframes.Data;
using Flowframes.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Flowframes.MiscUtils
{
    class FrameRename
    {
        public static bool framesAreRenamed;
        public static string[] importFilenames;   // index=renamed, value=original TODO: Store on disk instead for crashes?

        public static async Task Rename()
        {
            importFilenames = IoUtils.GetFilesSorted(Interpolate.currentSettings.framesFolder);

            Logger.Log($"Renaming {importFilenames.Length} frames...");
            Stopwatch benchmark = Stopwatch.StartNew();

            //int chunkSize = 2* (int)Math.Ceiling((double)importFilenames.Length / 4);// 2 * (int)Math.Ceiling((double)importFilenames.Length / 2 / Environment.ProcessorCount); // Make sure is even for 3D double frames
            int chunkSize = importFilenames.Length;
            List<Task> tasks = new List<Task>();

            for (int i = 0; i < importFilenames.Length; i += chunkSize)
            {
                int from = i; // capture variable
                tasks.Add(Task.Run(() => RenameCounterDirWorker(importFilenames, from, chunkSize)));
            }
            await Task.WhenAll(tasks);

            Logger.Log($"Renamed {importFilenames.Length} frames in {benchmark.ElapsedMilliseconds} ms", false, true);
            framesAreRenamed = true;
        }

        public static void RenameCounterDirWorker(string[] files, int from, int count)
        {
            int counter = Interpolate.currentSettings.is3D ? from / 2 : from;
            string dirA = Interpolate.currentSettings.framesFolder;
            string dirB = Paths.GetOtherDir(dirA);
            if (Interpolate.currentSettings.is3D)
                IoUtils.CreateDir(dirB);

            string dir = dirA;
            for (int i = from; i < from + count && i < files.Length; i++)
            {
                File.Move(files[i], Path.Combine(dir, counter.ToString().PadLeft(Padding.inputFramesRenamed, '0') + Path.GetExtension(files[i])));

                if (Interpolate.currentSettings.is3D)
                    if (dir == dirA)
                        dir = dirB;
                    else
                    {
                        dir = dirA;
                        counter++;
                    }
                else
                    counter++;
            }
        }

        public static async Task UnRename()
        {
            if (!framesAreRenamed) return;

            Logger.Log($"Unrenaming {importFilenames.Length} frames ...");
            Stopwatch benchmark = Stopwatch.StartNew();

            string[] files = IoUtils.GetFilesSorted(Interpolate.currentSettings.framesFolder);
            int chunkSize = files.Length;// (int)Math.Ceiling((double)files.Length / Environment.ProcessorCount * (Interpolate.currentSettings.is3D ? 2 : 1));
            List<Task> tasks = new List<Task>();

            for (int i = 0; i < files.Length; i += chunkSize)
            {
                int from = i; // capture variable
                //tasks.Add(Task.Run(() => UnRenameWorker(files, from, chunkSize, false)));
                UnRenameWorker(files, from, chunkSize, false);
            }

            if (Interpolate.currentSettings.is3D)
            {
                string[] filesB = IoUtils.GetFilesSorted(Paths.GetOtherDir(Interpolate.currentSettings.framesFolder));

                for (int i = 0; i < filesB.Length; i += chunkSize)
                {
                    int from = i; // capture variable
                    //tasks.Add(Task.Run(() => UnRenameWorker(filesB, from, chunkSize, true)));
                    UnRenameWorker(filesB, from, chunkSize, true);
                }
            }
            await Task.WhenAll(tasks);

            Logger.Log($"Unrenamed {importFilenames.Length} frames in {benchmark.ElapsedMilliseconds} ms", false, true);
            framesAreRenamed = false;
        }

        public static void UnRenameWorker(string[] files, int from, int count, bool isOther)
        {
            int multiplier = Interpolate.currentSettings.is3D ? 2 : 1;
            int offset = isOther ? 1 : 0;
            for (int i = from; i < from + count && i < files.Length; i++)
            {
                string movePath = Path.Combine(Interpolate.currentSettings.framesFolder, importFilenames[i * multiplier + offset]);
                File.Move(files[i], movePath);
            }
        }
    }
}
