using Flowframes.Data;
using Flowframes.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes.MiscUtils
{
    class FrameRename
    {
        public static bool framesAreRenamed = false;
        public static string[] importFilenames; // index=renamed, value=original
        public static int originalFrameSkipped = 0;

        public static async Task Rename(string lastEncodedOriginalInputFrame)
        {
            if (framesAreRenamed) return;

            importFilenames = IoUtils.GetFilesSorted(Interpolate.currentSettings.framesFolder);
            Logger.Log($"Renaming {importFilenames.Length} frames...");
            Stopwatch benchmark = Stopwatch.StartNew();

            int indexLast = string.IsNullOrEmpty(lastEncodedOriginalInputFrame) ? -1 : Array.IndexOf(importFilenames, lastEncodedOriginalInputFrame);
            if (indexLast != -1)
            {
                originalFrameSkipped = indexLast + (Interpolate.currentSettings.is3D ? 2 : 1);
                importFilenames = importFilenames.Skip(originalFrameSkipped).ToArray();

                // Shift scene change file names in synch with the input frames
                string sceneDir = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.scenesDir);
                string[] scenes = IoUtils.GetFilesSorted(sceneDir);
                int skippedOffset = Interpolate.currentSettings.is3D ? originalFrameSkipped / 2 : originalFrameSkipped;
                foreach (string sceneFullFileName in scenes)
                {
                    string sceneFilename = Path.GetFileNameWithoutExtension(sceneFullFileName);
                    int fileNo = int.Parse(sceneFilename);
                    string newFilename = fileNo >= skippedOffset ? (fileNo - skippedOffset).ToString().PadLeft(Padding.inputFrames, '0') : "!" + sceneFilename;
                    string targetPath = Path.Combine(Path.GetDirectoryName(sceneFullFileName), newFilename + Path.GetExtension(sceneFullFileName));
                    File.Move(sceneFullFileName, targetPath);
                }
            }
            else
                originalFrameSkipped = 0;

            //int chunkSize = 2* (int)Math.Ceiling((double)importFilenames.Length / 4);// 2 * (int)Math.Ceiling((double)importFilenames.Length / 2 / Environment.ProcessorCount); // Make sure is even for 3D double frames
            int chunkSize = importFilenames.Length;
            List<Task> tasks = new List<Task>();

            for (int i = 0; i < importFilenames.Length; i += chunkSize)
            {
                int from = i; // capture variable
                tasks.Add(Task.Run(() => RenameCounterDirWorker(importFilenames, from, chunkSize)));
            }
            await Task.WhenAll(tasks);

            Logger.Log($"Renamed {importFilenames.Length} frames in {FormatUtils.Time(benchmark.ElapsedMilliseconds)}", false, true);

            framesAreRenamed = true;
        }

        public static void RenameCounterDirWorker(string[] files, int from, int count)
        {
            int counter = Interpolate.currentSettings.is3D ? from / 2 : from;
            string dirA = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.framesWorkDir);
            IoUtils.CreateDir(dirA);
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

            if (originalFrameSkipped != 0)
            {
                // Shift back scene change file names in synch with the input frames
                string sceneDir = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.scenesDir);
                string[] scenes = IoUtils.GetFilesSorted(sceneDir);
                int skippedOffset = Interpolate.currentSettings.is3D ? originalFrameSkipped / 2 : originalFrameSkipped;
                foreach (string sceneFullFileName in scenes.Reverse())
                {
                    string sceneFilename = Path.GetFileNameWithoutExtension(sceneFullFileName);
                    string newFilename = sceneFilename[0] != '!' ? (int.Parse(sceneFilename) + skippedOffset).ToString().PadLeft(Padding.inputFrames, '0') : sceneFilename.Substring(1);
                    string targetPath = Path.Combine(Path.GetDirectoryName(sceneFullFileName), newFilename + Path.GetExtension(sceneFullFileName));
                    File.Move(sceneFullFileName, targetPath);
                }
            }

            string dirA = Path.Combine(Interpolate.currentSettings.tempFolder, Paths.framesWorkDir);
            string[] files = IoUtils.GetFilesSorted(dirA);
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
                string[] filesB = IoUtils.GetFilesSorted(Paths.GetOtherDir(dirA));

                for (int i = 0; i < filesB.Length; i += chunkSize)
                {
                    int from = i; // capture variable
                    //tasks.Add(Task.Run(() => UnRenameWorker(filesB, from, chunkSize, true)));
                    UnRenameWorker(filesB, from, chunkSize, true);
                }
            }
            await Task.WhenAll(tasks);

            Logger.Log($"Unrenamed {importFilenames.Length} frames in {FormatUtils.Time(benchmark.ElapsedMilliseconds)}", false, true);
            framesAreRenamed = false;
        }

        public static void UnRenameWorker(string[] files, int from, int count, bool isOther)
        {
            int multiplier = Interpolate.currentSettings.is3D ? 2 : 1;
            int offset = isOther ? 1 : 0;
            for (int i = from; i < from + count && i < files.Length; i++)
            {
                File.Move(files[i], importFilenames[i * multiplier + offset]);
            }
        }

        public static void LoadFilenames(string[] savedImportFilenames)
        {
            importFilenames = savedImportFilenames;
            framesAreRenamed = importFilenames != null && importFilenames.Length > 0 && !File.Exists(importFilenames[0]);
        }

        public static string GetOriginalFileName(int renamedIndex, bool otherOffset = false)
        {
            if (renamedIndex < 0)
                return null;
            if (Interpolate.currentSettings.is3D)
            {
                return renamedIndex * 2 < importFilenames.Length ? importFilenames[renamedIndex * 2 + (otherOffset ? 1 : 0)] : null;
            }
            else
            {
                return renamedIndex < importFilenames.Length ? importFilenames[renamedIndex] : null;
            }
        }

        // The scene frame no. is indexed in synch with the renamed frames
        public static string GetSceneChangeFileName(int renamedIndex)
        {
            if (renamedIndex < 0)
                return null;
            return renamedIndex.ToString().PadLeft(Padding.inputFrames, '0');
        }
    }
}
