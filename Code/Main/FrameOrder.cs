using Flowframes.Data;
using Flowframes.IO;
using Flowframes.MiscUtils;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Flowframes.Main
{
    class FrameOrder
    {
        static FileInfo[] frameFiles;
        static List<string> sceneFrames = new List<string>();
        static readonly ConcurrentDictionary<int, string> frameFileContents = new ConcurrentDictionary<int, string>();
        static readonly ConcurrentDictionary<int, List<string>> inputFilenames = new ConcurrentDictionary<int, List<string>>();

        public static async Task CreateFrameOrderFile(string tempFolder, bool loopEnabled, float interpFactor)
        {
            Logger.Log("Generating frame order information...");

            try
            {
                foreach (FileInfo file in IoUtils.GetFileInfosSorted(tempFolder, false, $"{Paths.frameOrderPrefix}*.*"))
                    file.Delete();

                Stopwatch benchmark = Stopwatch.StartNew();

                if (Interpolate.currentSettings.ai.NameInternal == Implementations.rifeNcnnVs.NameInternal)
                    CreateFramesFileVid(Interpolate.currentSettings.inPath, Interpolate.currentSettings.tempFolder, loopEnabled, interpFactor);
                else
                    await CreateFramesFileImgSeq(tempFolder, loopEnabled, interpFactor);

                Logger.Log($"Generating frame order information... Done.", false, true);
                Logger.Log($"Generated frame order info file in {benchmark.ElapsedMilliseconds} ms", true);
            }
            catch (Exception e)
            {
                Logger.Log($"Error generating frame order information: {e.Message}\n{e.StackTrace}");
            }
        }

        static Dictionary<string, List<string>> dupesDict = new Dictionary<string, List<string>>();

        static void LoadDupesFile(string path)
        {
            dupesDict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(path));
        }

        public static void CreateFramesFileVid(string vidPath, string tempFolder, bool loop, float interpFactor)
        {
            if (Interpolate.canceled) return;
            Logger.Log($"Generating frame order information for {interpFactor}x...", false, true);

            // frameFileContents.Clear();
            int lastOutFileCount = 0;

            string inputJsonPath = Path.Combine(tempFolder, "input.json");
            List<int> inputFrames = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(inputJsonPath));

            int frameCount = inputFrames.Count;

            // if (loop)
            // {
            //     frameCount++;
            // }

            string dupesFile = Path.Combine(tempFolder, "dupes.test.json");
            var dupes = JsonConvert.DeserializeObject<Dictionary<int, List<int>>>(File.ReadAllText(dupesFile));

            int targetFrameCount = (frameCount * interpFactor).RoundToInt() - InterpolateUtils.GetRoundedInterpFramesPerInputFrame(interpFactor);

            Fraction step = new Fraction(frameCount, targetFrameCount + InterpolateUtils.GetRoundedInterpFramesPerInputFrame(interpFactor));

            var framesList = new List<int>();

            for (int i = 0; i < targetFrameCount; i++)
            {
                float currentFrameTime = 1 + (step * i).GetFloat();
                int sourceFrameIdx = (int)Math.Floor(currentFrameTime) - 1;

                framesList.Add(i);
                Console.WriteLine($"Frame: #{i} - Idx: {sourceFrameIdx} - [Time: {currentFrameTime}]");

                if (sourceFrameIdx < dupes.Count)
                {
                    bool last = i == lastOutFileCount;

                    if (last && loop)
                        continue;

                    for (int dupeNum = 0; dupeNum < dupes.ElementAt(sourceFrameIdx).Value.Count; dupeNum++)
                    {
                        framesList.Add(framesList.Last());
                        Console.WriteLine($"Frame: #{i} - Idx: {sourceFrameIdx} - (Dupe {dupeNum + 1}/{dupes.ElementAt(sourceFrameIdx).Value.Count})");
                    }
                }
            }

            // if (loop)
            // {
            //     framesList.Add(framesList.First());
            // }

            //for (int x = 0; x < frameFileContents.Count; x++)
            //    fileContent += frameFileContents[x];

            if (Config.GetBool(Config.Key.fixOutputDuration)) // Match input duration by padding duping last frame until interp frames == (inputframes * factor)
            {
                int neededFrames = (frameCount * interpFactor).RoundToInt() - framesList.Count;

                for (int i = 0; i < neededFrames; i++)
                    framesList.Add(framesList.Last());
            }

            if (loop)
                framesList.RemoveAt(framesList.Count - 1);

            string framesFileVs = Path.Combine(tempFolder, "frames.vs.json");
            // List<int> frameNums = new List<int>();
            // 
            // foreach (string line in fileContent.SplitIntoLines().Where(x => x.StartsWith("file ")))
            //     frameNums.Add(line.Split('/')[1].Split('.')[0].GetInt() - 1); // Convert filename to 0-indexed number

            File.WriteAllText(framesFileVs, JsonConvert.SerializeObject(framesList, Formatting.Indented));
        }

        public static async Task CreateFramesFileImgSeq(string tempFolder, bool loop, float interpFactor)
        {
            // await CreateFramesFileVideo(Interpolate.currentSettings.inPath, loop, interpFactor);

            if (Interpolate.canceled) return;
            Logger.Log($"Generating frame order information for {interpFactor}x...", false, true);

            bool sceneDetection = true;

            frameFileContents.Clear();

            string framesDir = Path.Combine(tempFolder, Paths.framesDir);
            frameFiles = new DirectoryInfo(framesDir).GetFiles("*" + Interpolate.currentSettings.framesExt);
            string framesFile = Path.Combine(tempFolder, Paths.GetFrameOrderFilename(interpFactor));
            string fileContent = "";
            string dupesFile = Path.Combine(tempFolder, "dupes.json");
            LoadDupesFile(dupesFile);

            string scnFramesPath = Path.Combine(tempFolder, Paths.scenesDir);

            sceneFrames.Clear();

            if (Directory.Exists(scnFramesPath))
                sceneFrames = Directory.GetFiles(scnFramesPath).Select(file => GetNameNoExt(file)).ToList();

            inputFilenames.Clear();
            bool debug = true; // Config.GetBool("frameOrderDebug", false);
            List<Task> tasks = new List<Task>();
            int linesPerTask = (400 / interpFactor).RoundToInt();
            int num = 0;

            int targetFrameCount = (frameFiles.Length * interpFactor).RoundToInt() - InterpolateUtils.GetRoundedInterpFramesPerInputFrame(interpFactor);

            if (interpFactor == (int)interpFactor) // Use old multi-threaded code if factor is not fractional
            {
                for (int i = 0; i < frameFiles.Length; i += linesPerTask)
                {
                    int startIndex = i; // capture value
                    int taskNo = num; // capture value
                    tasks.Add(Task.Run(() => GenerateFrameLines(taskNo, startIndex, linesPerTask, (int)interpFactor, sceneDetection, debug)));
                    num++;
                }
            }
            else
            {
                tasks.Add(Task.Run(() => GenerateFrameLinesFloat(frameFiles.Length, targetFrameCount, interpFactor, sceneDetection, debug)));
            }

            await Task.WhenAll(tasks);

            for (int x = 0; x < frameFileContents.Count; x++)
                fileContent += frameFileContents[x];

            if (Config.GetBool(Config.Key.fixOutputDuration)) // Match input duration by padding duping last frame until interp frames == (inputframes * factor)
            {
                string[] lines = fileContent.SplitIntoLines();
                int neededFrames = (frameFiles.Length * interpFactor).RoundToInt() - lines.Where(x => x.StartsWith("file ")).Count() / (Interpolate.currentSettings.is3D ? 2 : 1);
                IEnumerable<string> fileLines = lines.Where(x => x.StartsWith("file ")).Reverse();

                for (int i = 0; i < neededFrames; i++)
                {
                    if (Interpolate.currentSettings.is3D)
                    {
                        fileContent += fileLines.ElementAt(1) + "\n";
                        fileContent += lines.Where(x => x.StartsWith("duration ")).Last() + "\n";
                    }
                    fileContent += fileLines.First() + "\n";
                    fileContent += lines.Where(x => x.StartsWith("duration ")).Last() + "\n";
                }
            }

            if (loop)
                fileContent = fileContent.Remove(fileContent.LastIndexOf('\n'));

            File.WriteAllText(framesFile, fileContent);
            List<string> allInputFilenames = new List<string>();
            for (int x = 0; x < inputFilenames.Count; x++)
                allInputFilenames.AddRange(inputFilenames[x]);

            File.WriteAllText(framesFile + ".inputframes.json", JsonConvert.SerializeObject(allInputFilenames, Formatting.Indented));

            string framesFileVs = Path.Combine(tempFolder, "frames.vs.json");
            List<int> frameNums = new List<int>();

            foreach (string line in fileContent.SplitIntoLines().Where(x => x.StartsWith("file ")))
                frameNums.Add(line.Split('/')[1].Split('.')[0].GetInt() - 1); // Convert filename to 0-indexed number

            File.WriteAllText(framesFileVs, JsonConvert.SerializeObject(frameNums, Formatting.Indented));
        }

        class FrameFileLine
        {
            public string OutFileName { get; set; } = "";
            public string InFileNameFrom { get; set; } = "";
            public string InFileNameTo { get; set; } = "";
            public string InFileNameFromNext { get; set; } = "";
            public string InFileNameToNext { get; set; } = "";
            public float Timestep { get; set; } = -1;
            public bool Discard { get; set; } = false;
            public bool DiscardNext { get; set; } = false;
            public int DiscardedFrames { get; set; } = 0;

            public FrameFileLine(string outFileName, string inFilenameFrom, string inFilenameTo, string inFilenameToNext, float timestep, bool discard = false, bool discardNext = false, int discardedFrames = 0)
            {
                OutFileName = outFileName;
                InFileNameFrom = inFilenameFrom;
                InFileNameTo = inFilenameTo;
                InFileNameFromNext = inFilenameTo;
                InFileNameToNext = inFilenameToNext;
                Timestep = timestep;
                Discard = discard;
                DiscardNext = discardNext;
                DiscardedFrames = discardedFrames;
            }

            public override string ToString()
            {
                List<string> strings = new List<string>();

                if (!string.IsNullOrWhiteSpace(InFileNameTo)) strings.Add($"to {InFileNameTo}");
                if (Timestep >= 0f) strings.Add($"@ {Timestep.ToString("0.000000").Split('.').Last()}");
                if (Discard) strings.Add("[Discard]");
                if (DiscardNext) strings.Add($"SCN:{InFileNameFromNext}>{InFileNameToNext}>{DiscardedFrames}");

                return $"file '{OutFileName}' # => {InFileNameFrom} {string.Join(" ", strings)}\n";
            }
        }

        static void GenerateFrameLinesFloat(int sourceFrameCount, int targetFrameCount, float factor, bool sceneDetection, bool debug)
        {
            bool blendSceneChances = Config.GetInt(Config.Key.sceneChangeFillMode) > 0;
            string ext = Interpolate.currentSettings.interpExt;
            Fraction step = new Fraction(sourceFrameCount, targetFrameCount + InterpolateUtils.GetRoundedInterpFramesPerInputFrame(factor));

            List<FrameFileLine> lines = new List<FrameFileLine>();

            string lastUndiscardFrame = "";

            for (int i = 0; i < targetFrameCount; i++)
            {
                if (Interpolate.canceled) return;

                float currentFrameTime = 1 + (step * i).GetFloat();
                int sourceFrameIdx = (int)Math.Floor(currentFrameTime) - 1;
                float timestep = (currentFrameTime - (int)Math.Floor(currentFrameTime));
                bool sceneChange = (sceneDetection && (sourceFrameIdx + 1) < FrameRename.importFilenames.Length && sceneFrames.Contains(GetNameNoExt(FrameRename.importFilenames[sourceFrameIdx + 1])));
                string filename = $"{Paths.interpDir}/{(i + 1).ToString().PadLeft(Padding.interpFrames, '0')}{ext}";

                if (string.IsNullOrWhiteSpace(lastUndiscardFrame))
                    lastUndiscardFrame = filename;

                if (!sceneChange)
                    lastUndiscardFrame = filename;

                string inputFilenameFrom = frameFiles[sourceFrameIdx].Name;
                string inputFilenameTo = (sourceFrameIdx + 1 >= frameFiles.Length) ? "" : frameFiles[sourceFrameIdx + 1].Name;
                string inputFilenameToNext = (sourceFrameIdx + 2 >= frameFiles.Length) ? "" : frameFiles[sourceFrameIdx + 2].Name;

                Console.WriteLine($"Frame: Idx {sourceFrameIdx} - {(sceneChange && !blendSceneChances ? lastUndiscardFrame : filename)}");
                lines.Add(new FrameFileLine(sceneChange && !blendSceneChances ? lastUndiscardFrame : filename, inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChange));

                string inputFilenameNoExtRenamed = Path.GetFileNameWithoutExtension(FrameRename.importFilenames[sourceFrameIdx]);

                if (!dupesDict.ContainsKey(inputFilenameNoExtRenamed))
                    continue;

                foreach (string s in dupesDict[inputFilenameNoExtRenamed])
                {
                    string fname = sceneChange && !blendSceneChances ? lastUndiscardFrame : filename;
                    Console.WriteLine($"Frame: Idx {sourceFrameIdx} - Dupe {dupesDict[inputFilenameNoExtRenamed].IndexOf(s)}/{dupesDict[inputFilenameNoExtRenamed].Count} {fname}");
                    lines.Add(new FrameFileLine(fname, inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChange));
                }
            }

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                bool discardNext = lineIdx > 0 && (lineIdx + 1) < lines.Count && !lines.ElementAt(lineIdx).Discard && lines.ElementAt(lineIdx + 1).Discard;
                int discardedFramesCount = 0;

                if (discardNext)
                {
                    for (int idx = lineIdx + 1; idx < lines.Count; idx++)
                    {
                        if (lines.ElementAt(idx).Discard)
                            discardedFramesCount++;
                        else
                            break;
                    }
                }

                lines.ElementAt(lineIdx).DiscardNext = discardNext;
                lines.ElementAt(lineIdx).DiscardedFrames = discardedFramesCount;
            }

            frameFileContents[0] = String.Join("", lines);
        }

        static void GenerateFrameLines(int number, int startIndex, int count, int factor, bool sceneDetection, bool debug)
        {
            int totalFileCount = (startIndex) * factor;
            int interpFramesAmount = factor;
            string ext = Interpolate.currentSettings.interpExt;

            string fileContent = "";
            List<string> currentinputFilenames = new List<string>();
            inputFilenames[number] = currentinputFilenames;

            for (int i = startIndex; i < (startIndex + count); i++)
            {
                if (Interpolate.canceled) return;
                if (i >= frameFiles.Length) break;

                string frameName = GetNameNoExt(frameFiles[i].Name);
                string origFrameName = GetNameNoExt(FrameRename.importFilenames[Interpolate.currentSettings.is3D ? i * 2 : i]);
                int dupesAmount = dupesDict.TryGetValue(origFrameName, out List<string> value) ? value.Count : 0;
                bool noInterpolationAfterThisFrame = false;
                if (sceneDetection && (i + 1) < FrameRename.importFilenames.Length)
                {
                    // in 3D the scenes are half the total frames, so use the index in renamed frames
                    string origNextFrameName = Interpolate.currentSettings.is3D ? (i + 1).ToString().PadLeft(Padding.inputFrames, '0') : GetNameNoExt(FrameRename.importFilenames[i + 1]);
                    if (sceneFrames.Contains(origNextFrameName))
                        noInterpolationAfterThisFrame = true;
                }

                if (i == frameFiles.Length - 1)
                    interpFramesAmount = 1;
                for (int frm = 0; frm < interpFramesAmount; frm++)  // Generate frames file lines
                {
                    if (noInterpolationAfterThisFrame)     // If next frame is scene cut frame
                    {
                        string scnChangeNote = $"SCN:{frameFiles[i]}>{frameFiles[i + 1]}";

                        totalFileCount++;
                        fileContent = WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [ Source ]", scnChangeNote);

                        if (Config.GetInt(Config.Key.sceneChangeFillMode) == 0)      // Duplicate last frame
                        {
                            int lastNum = totalFileCount;

                            for (int dupeCount = 1; dupeCount < interpFramesAmount; dupeCount++)
                            {
                                totalFileCount++;
                                fileContent = WriteFrameWithDupes(dupesAmount, fileContent, lastNum, ext, debug, $"[In: {frameName}] [DISCARDED]");
                            }
                        }
                        else
                        {
                            for (int dupeCount = 1; dupeCount < interpFramesAmount; dupeCount++)
                            {
                                totalFileCount++;
                                fileContent = WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [BLEND FRAME]");
                            }
                        }

                        frm = interpFramesAmount;
                    }
                    else
                    {
                        totalFileCount++;
                        fileContent = WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [{((frm == 0) ? " Source " : $"Interp {frm}")}]");
                    }

                    currentinputFilenames.Add(frameFiles[i].Name);
                }
            }

            frameFileContents[number] = fileContent;
        }

        static string WriteFrameWithDupes(int dupesAmount, string fileContent, int frameNum, string ext, bool debug, string debugNote = "", string forcedNote = "")
        {
            string duration = $"duration {1f / Interpolate.currentSettings.outFps.GetFloat()}";
            for (int writtenDupes = -1; writtenDupes < dupesAmount; writtenDupes++)      // Write duplicates
            {
                fileContent += $"file '{Paths.interpDir}/{frameNum.ToString().PadLeft(Padding.interpFrames, '0')}{ext}' # {(debug ? ($"Dupe {writtenDupes + 1:000} {debugNote}").Replace("Dupe 000", "        ") : "")}{forcedNote}\n";
                fileContent += $"{duration}\n";
                if (Interpolate.currentSettings.is3D)
                {
                    fileContent += $"file '{Paths.GetOtherDir(Paths.interpDir)}/{frameNum.ToString().PadLeft(Padding.interpFrames, '0')}{ext}' # {(debug ? ($"Dupe {writtenDupes + 1:000} {debugNote}").Replace("Dupe 000", "        ") : "")}{forcedNote}\n";
                    fileContent += $"{duration}\n";
                }
            }

            return fileContent;
        }

        static string GetNameNoExt(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
