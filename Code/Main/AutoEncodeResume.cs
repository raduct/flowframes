using Flowframes.Data;
using Flowframes.IO;
using Flowframes.MiscUtils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using I = Flowframes.Interpolate;

namespace Flowframes.Main
{
    class AutoEncodeResume
    {
        public static List<string> processedInputFrames = new List<string>();
        public static int encodedChunks = 0;
        public static int encodedFrames = 0;

        public static bool resumeNextRun;
        private const string interpSettingsFilename = "settings.json";
        private const string chunksFilename = "chunks.json";
        private const string inputFramesFilename = "input-frames.json";
        private const string frameRenameFilename = "rename-frames.json";

        public static void Reset()
        {
            processedInputFrames = new List<string>();
            encodedChunks = 0;
            encodedFrames = 0;
            SaveGlobal(true);
        }

        public static void SaveChunk()
        {
            string saveDir = Path.Combine(I.currentSettings.tempFolder, Paths.resumeDir);
            Directory.CreateDirectory(saveDir);

            string chunksJsonPath = Path.Combine(saveDir, chunksFilename);
            Dictionary<string, string> saveData = new Dictionary<string, string>();
            saveData.Add("encodedChunks", encodedChunks.ToString());
            saveData.Add("encodedFrames", encodedFrames.ToString());
            File.WriteAllText(chunksJsonPath, JsonConvert.SerializeObject(saveData, Formatting.Indented));

            string inputFramesJsonPath = Path.Combine(saveDir, inputFramesFilename);
            File.WriteAllText(inputFramesJsonPath, JsonConvert.SerializeObject(processedInputFrames, Formatting.Indented));
        }

        public static void SaveGlobal(bool saveSettings = false)
        {
            string saveDir = Path.Combine(I.currentSettings.tempFolder, Paths.resumeDir);
            Directory.CreateDirectory(saveDir);

            if (saveSettings)
            {
                // Save current settings
                string settingsJsonPath = Path.Combine(saveDir, interpSettingsFilename);
                File.WriteAllText(settingsJsonPath, JsonConvert.SerializeObject(I.currentSettings, Formatting.Indented));

                SaveChunk();
            }

            // Save input frames rename
            string frameRenameFilenamePath = Path.Combine(saveDir, frameRenameFilename);
            if (FrameRename.framesAreRenamed)
                File.WriteAllText(frameRenameFilenamePath, JsonConvert.SerializeObject(FrameRename.importFilenames, Formatting.Indented));
            else
                IoUtils.TryDeleteIfExists(frameRenameFilenamePath);
        }

        public static void LoadTempFolder(string tempFolderPath)
        {
            try
            {
                string resumeFolderPath = Path.Combine(tempFolderPath, Paths.resumeDir);
                string settingsJsonPath = Path.Combine(resumeFolderPath, interpSettingsFilename);
                InterpSettings interpSettings = JsonConvert.DeserializeObject<InterpSettings>(File.ReadAllText(settingsJsonPath));
                Program.mainForm.LoadBatchEntry(interpSettings);

                string frameRenameFilenamePath = Path.Combine(resumeFolderPath, frameRenameFilename);
                if (File.Exists(frameRenameFilenamePath))
                {
                    string[] importFilenames = JsonConvert.DeserializeObject<string[]>(File.ReadAllText(frameRenameFilenamePath));
                    FrameRename.LoadFilenames(importFilenames);
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to load resume data: {e.Message}\n{e.StackTrace}");
                resumeNextRun = false;
            }
        }

        // Remove already interpolated data, return true if interpolation should be skipped
        public static async Task<bool> PrepareResumedRun()
        {
            if (!resumeNextRun) return false;

            try
            {
                string chunkJsonPath = Path.Combine(I.currentSettings.tempFolder, Paths.resumeDir, chunksFilename);
                // abort if no chunks saved
                if (!File.Exists(chunkJsonPath))
                    return false;

                string videoChunksFolder = Path.Combine(I.currentSettings.tempFolder, Paths.chunksDir);
                dynamic chunksData = JsonConvert.DeserializeObject(File.ReadAllText(chunkJsonPath));
                encodedChunks = chunksData.encodedChunks;
                encodedFrames = chunksData.encodedFrames;

                // remove unfinished chunks
                IEnumerable<FileInfo> invalidChunks = IoUtils.GetFileInfosSorted(videoChunksFolder, true, "????.*").Skip(encodedChunks);
                foreach (FileInfo chunk in invalidChunks)
                    chunk.Delete();

                await FrameRename.UnRename();

                string inFramesJsonPath = Path.Combine(I.currentSettings.tempFolder, Paths.resumeDir, inputFramesFilename);
                if (File.Exists(inFramesJsonPath))
                {
                    processedInputFrames = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(inFramesJsonPath));
                    IEnumerable<string> uniqueProcessedInputFrames = processedInputFrames.Distinct();
                    int uniqueInputFrames = uniqueProcessedInputFrames.Count();

                    // Remove complete frames and scene changes
                    string sceneDir = Path.Combine(I.currentSettings.tempFolder, Paths.scenesDir);
                    string framesDir = Path.Combine(I.currentSettings.tempFolder, Paths.framesDir);
                    string framesOtherDir = Paths.GetOtherDir(framesDir);
                    int i = 0;
                    foreach (string inputFrameName in uniqueProcessedInputFrames)
                    {
                        IoUtils.TryDeleteIfExists(Path.Combine(sceneDir, FrameRename.GetSceneChangeFileName(i) + I.currentSettings.framesExt));
                        IoUtils.TryDeleteIfExists(FrameRename.GetOriginalFileName(i));
                        if (I.currentSettings.is3D)
                            IoUtils.TryDeleteIfExists(FrameRename.GetOriginalFileName(i, true));
                        i++;
                    }
                    FrameRename.TrimFirst(uniqueInputFrames);

                    // Shift scene change file names in synch with the input frames
                    string[] remainingScenes = IoUtils.GetFilesSorted(sceneDir);
                    foreach (string sceneFullFileName in remainingScenes)
                    {
                        string sceneFilename = Path.GetFileNameWithoutExtension(sceneFullFileName);
                        int fileNo = int.Parse(sceneFilename);
                        string newFilename = (fileNo - uniqueInputFrames).ToString().PadLeft(Padding.inputFrames, '0');
                        string targetPath = Path.Combine(Path.GetDirectoryName(sceneFullFileName), newFilename + Path.GetExtension(sceneFullFileName));
                        File.Move(sceneFullFileName, targetPath);
                    }
                }
                processedInputFrames = new List<string>();
                SaveGlobal(true);

                int inputFramesLeft = IoUtils.GetAmountOfFiles(Path.Combine(I.currentSettings.tempFolder, Paths.framesDir), false);

                Logger.Log($"Resume: Already encoded {encodedFrames} frames in {encodedChunks} chunks. There are now {inputFramesLeft} input frames left to interpolate.");

                if (inputFramesLeft < 2)
                {
                    if (IoUtils.GetAmountOfFiles(videoChunksFolder, true, "*.*") > 0)
                    {
                        Logger.Log($"No more frames left to interpolate - Merging existing video chunks instead.");
                        await Export.ChunksToVideo(I.currentSettings.tempFolder, videoChunksFolder, I.currentSettings.outPath);
                        await I.Done();
                    }
                    else
                    {
                        I.Cancel("There are no more frames left to interpolate in this temp folder!");
                    }

                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to prepare resumed run: {e.Message}\n{e.StackTrace}");
                I.Cancel("Failed to resume interpolation. Check the logs for details.");
                resumeNextRun = false;
                return true;
            }

            // string stateFilepath = Path.Combine(I.current.tempFolder, Paths.resumeDir, resumeFilename);
            // ResumeState state = new ResumeState(File.ReadAllText(stateFilepath));
            // 
            // string fileMapFilepath = Path.Combine(I.current.tempFolder, Paths.resumeDir, filenameMapFilename);
            // List<string> inputFrameLines = File.ReadAllLines(fileMapFilepath).Where(l => l.Trim().Length > 3).ToList();
            // List<string> inputFrames = inputFrameLines.Select(l => Path.Combine(I.current.framesFolder, l.Split('|')[1])).ToList();
            // 
            // for (int i = 0; i < state.interpolatedInputFrames; i++)
            // {
            //     IoUtils.TryDeleteIfExists(inputFrames[i]);
            //     if (i % 1000 == 0) await Task.Delay(1);
            // }
            // 
            // Directory.Move(I.current.interpFolder, I.current.interpFolder + Paths.prevSuffix);  // Move existing interp frames
            // Directory.CreateDirectory(I.current.interpFolder);  // Re-create empty interp folder
        }
    }
}
