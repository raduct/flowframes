using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Magick;
using Flowframes.MiscUtils;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flowframes.Main
{
    class FrameOrder
    {
        public const string inputFramesJson = ".inputframes.json";

        FileInfo[] frameFiles;
        readonly List<string> sceneFrames = new List<string>();
        readonly ConcurrentDictionary<int, string> frameFileContents = new ConcurrentDictionary<int, string>();
        readonly ConcurrentDictionary<int, List<string>> inputFilenames = new ConcurrentDictionary<int, List<string>>();
        Dictionary<string, List<string>> dupesDict;

        static string durationLine;

        public static async Task CreateFrameOrderFile(string tempFolder, bool loopEnabled, float interpFactor)
        {
            Logger.Log("Generating frame order information...");

            try
            {
                foreach (string file in Directory.GetFiles(tempFolder, $"{Paths.frameOrderPrefix}*.*"))
                    File.Delete(file);

                Stopwatch benchmark = Stopwatch.StartNew();

                if (Interpolate.currentSettings.ai.NameInternal == Implementations.rifeNcnnVs.NameInternal)
                    CreateFramesFileVid(Interpolate.currentSettings.inPath, Interpolate.currentSettings.tempFolder, loopEnabled, interpFactor);
                else
                    await new FrameOrder().CreateFramesFileImgSeq(tempFolder, loopEnabled, interpFactor);

                Logger.Log($"Generated frame order info file in {FormatUtils.Time(benchmark.ElapsedMilliseconds)}", false, true);
            }
            catch (Exception e)
            {
                Logger.Log($"Error generating frame order information: {e.Message}\n{e.StackTrace}");
            }
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

        async Task CreateFramesFileImgSeq(string tempFolder, bool loop, float interpFactor)
        {
            // await CreateFramesFileVideo(Interpolate.currentSettings.inPath, loop, interpFactor);

            if (Interpolate.canceled) return;
            Logger.Log($"Generating frame order information for {interpFactor}x...", false, true);

            bool sceneDetection = true;

            frameFileContents.Clear();

            frameFiles = new DirectoryInfo(Path.Combine(tempFolder, Paths.framesWorkDir)).GetFiles("*" + Interpolate.currentSettings.framesExt);
            string framesFile = Path.Combine(tempFolder, Paths.GetFrameOrderFilename(interpFactor));
            dupesDict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(Path.Combine(tempFolder, Dedupe.dupesFileName)));

            string scnFramesPath = Path.Combine(tempFolder, Paths.scenesDir);

            sceneFrames.Clear();

            if (Directory.Exists(scnFramesPath))
                sceneFrames.AddRange(Directory.GetFiles(scnFramesPath).Select(file => Path.GetFileNameWithoutExtension(file)));

            inputFilenames.Clear();
            bool debug = true; // Config.GetBool("frameOrderDebug", false);
            durationLine = $"duration {1f / Interpolate.currentSettings.outFps.GetFloat()}\n";
            List<Task> tasks = new List<Task>();

            if (interpFactor == (int)interpFactor) // Use old multi-threaded code if factor is not fractional
            {
                int linesPerTask = (600 / interpFactor).RoundToInt();
                int num = 0;
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
                tasks.Add(Task.Run(() => GenerateFrameLinesFloat(interpFactor, sceneDetection, debug)));
            }

            await Task.WhenAll(tasks);

            StringBuilder fileContent = new StringBuilder();
            for (int x = 0; x < frameFileContents.Count; x++)
                fileContent.Append(frameFileContents[x]);

            if (Config.GetBool(Config.Key.fixOutputDuration)) // Match input duration by padding duping last frame until interp frames == (inputframes * factor)
            {
                string[] lines = fileContent.ToString().SplitIntoLines();
                IEnumerable<string> fileLines = lines.Where(x => x.StartsWith("file ")).Reverse();
                int neededFrames = (frameFiles.Length * interpFactor).RoundToInt() - fileLines.Count() / (Interpolate.currentSettings.is3D ? 2 : 1);

                for (int i = 0; i < neededFrames; i++)
                {
                    if (Interpolate.currentSettings.is3D)
                    {
                        fileContent.Append(fileLines.ElementAt(1) + "\n");
                        fileContent.Append(durationLine);
                    }
                    fileContent.Append(fileLines.First() + "\n");
                    fileContent.Append(durationLine);
                }
            }

            string stringFileContent = fileContent.ToString();
            if (loop)
                stringFileContent = stringFileContent.Remove(stringFileContent.LastIndexOf('\n'));

            File.WriteAllText(framesFile, stringFileContent);

            List<string> allInputFilenames = new List<string>();
            for (int x = 0; x < inputFilenames.Count; x++)
                allInputFilenames.AddRange(inputFilenames[x]);

            File.WriteAllText(framesFile + inputFramesJson, JsonConvert.SerializeObject(allInputFilenames, Formatting.Indented));
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

                if (!string.IsNullOrWhiteSpace(InFileNameTo))
                {
                    strings.Add($"to {InFileNameTo}");
                    if (Timestep >= 0f)
                        strings.Add($"@ {Timestep:0.000000}");
                }
                if (Discard) strings.Add("[Discard]");
                if (DiscardNext) strings.Add($"SCN:{InFileNameFromNext}>{InFileNameToNext}>{DiscardedFrames}");

                return $"file '{OutFileName}' # => {InFileNameFrom} {string.Join(" ", strings)}\n"
                    + durationLine;
            }
        }

        void GenerateFrameLinesFloat(float factor, bool sceneDetection, bool debug)
        {
            bool blendSceneChances = Config.GetInt(Config.Key.sceneChangeFillMode) > 0;
            string ext = Interpolate.currentSettings.interpExt;

            int targetFrameCount = (int)Math.Ceiling(factor * (frameFiles.Length - 1)) + 1;

            List<FrameFileLine> lines = new List<FrameFileLine>();

            string lastUndiscardFrame = "";

            for (int i = 0; i < targetFrameCount; i++)
            {
                if (Interpolate.canceled) return;

                float currentFrameTime = i / factor;
                int sourceFrameIdx = (int)Math.Floor(currentFrameTime);
                float timestep = currentFrameTime - sourceFrameIdx;

                bool sceneChangeNextFrame = false;
                if (sceneDetection)
                {
                    string origNextFrameName = FrameRename.GetSceneChangeFileName(sourceFrameIdx + 1);
                    if (origNextFrameName != null && sceneFrames.Contains(origNextFrameName))
                        sceneChangeNextFrame = true;
                }

                string filename = $"{(i + 1).ToString().PadLeft(Padding.interpFrames, '0')}{ext}";

                if (string.IsNullOrWhiteSpace(lastUndiscardFrame))
                    lastUndiscardFrame = filename;

                if (!sceneChangeNextFrame)
                    lastUndiscardFrame = filename;

                string inputFilenameFrom = frameFiles[sourceFrameIdx].Name;
                string inputFilenameTo = (sourceFrameIdx + 1 >= frameFiles.Length) ? "" : frameFiles[sourceFrameIdx + 1].Name;
                string inputFilenameToNext = (sourceFrameIdx + 2 >= frameFiles.Length) ? "" : frameFiles[sourceFrameIdx + 2].Name;

                string fname = sceneChangeNextFrame && !blendSceneChances ? lastUndiscardFrame : filename;
                if (debug)
                    Console.WriteLine($"Frame: Idx {sourceFrameIdx} - {fname}");
                lines.Add(new FrameFileLine($"{Paths.interpDir}/{fname}", inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChangeNextFrame));
                if (Interpolate.currentSettings.is3D)
                    lines.Add(new FrameFileLine($"{Paths.GetOtherDir(Paths.interpDir)}/{fname}", inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChangeNextFrame));

                string inputFilenameNoExtRenamed = Path.GetFileNameWithoutExtension(FrameRename.GetOriginalFileName(sourceFrameIdx));
                int dupesNo = inputFilenameNoExtRenamed != null && dupesDict.TryGetValue(inputFilenameNoExtRenamed, out List<string> value) ? value.Count : 0;

                for (int j = 0; j < dupesNo; j++)
                {
                    if (debug)
                        Console.WriteLine($"Frame: Idx {sourceFrameIdx} - Dupe {j}/{dupesNo} {fname}");
                    lines.Add(new FrameFileLine($"{Paths.interpDir}/{fname}", inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChangeNextFrame));
                    if (Interpolate.currentSettings.is3D)
                        lines.Add(new FrameFileLine($"{Paths.GetOtherDir(Paths.interpDir)}/{fname}", inputFilenameFrom, inputFilenameTo, inputFilenameToNext, timestep, sceneChangeNextFrame));
                }
            }

            List<string> currentinputFilenames = new List<string>();

            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                bool discardNext = (lineIdx + 1) < lines.Count && !lines.ElementAt(lineIdx).Discard && lines.ElementAt(lineIdx + 1).Discard;
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
                    lines.ElementAt(lineIdx).DiscardNext = discardNext;
                    lines.ElementAt(lineIdx).DiscardedFrames = discardedFramesCount;
                }

                currentinputFilenames.Add(lines.ElementAt(lineIdx).InFileNameFrom);
            }

            frameFileContents[0] = string.Join("", lines);

            inputFilenames[0] = currentinputFilenames;
        }

        void GenerateFrameLines(int number, int startIndex, int count, int factor, bool sceneDetection, bool debug)
        {
            int totalFileCount = startIndex * factor;
            int interpFramesAmount = factor;
            string ext = Interpolate.currentSettings.interpExt;

            StringBuilder fileContent = new StringBuilder();
            List<string> currentinputFilenames = new List<string>();
            inputFilenames[number] = currentinputFilenames;

            int savedTotalFileCount = totalFileCount;
            for (int i = startIndex; i < (startIndex + count); i++)
            {
                if (Interpolate.canceled) return;
                if (i >= frameFiles.Length) break;

                string frameName = Path.GetFileNameWithoutExtension(frameFiles[i].Name);
                string origFrameName = Path.GetFileNameWithoutExtension(FrameRename.GetOriginalFileName(i));
                int dupesAmount = origFrameName != null && dupesDict.TryGetValue(origFrameName, out List<string> value) ? value.Count : 0;
                bool sceneChangeNextFrame = false;
                if (sceneDetection)
                {
                    string origNextFrameName = FrameRename.GetSceneChangeFileName(i + 1);
                    if (origNextFrameName != null && sceneFrames.Contains(origNextFrameName))
                        sceneChangeNextFrame = true;
                }

                if (i == frameFiles.Length - 1)
                    interpFramesAmount = 1;
                for (int frm = 0; frm < interpFramesAmount; frm++)  // Generate frames file lines
                {
                    if (sceneChangeNextFrame)     // If next frame is scene cut frame
                    {
                        string scnChangeNote = $"SCN:{frameFiles[i]}>{frameFiles[i + 1]}";

                        totalFileCount++;
                        WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [ Source ]", scnChangeNote);

                        if (Config.GetInt(Config.Key.sceneChangeFillMode) == 0)      // Duplicate last frame
                        {
                            int lastNum = totalFileCount;

                            for (int dupeCount = 1; dupeCount < interpFramesAmount; dupeCount++)
                            {
                                totalFileCount++;
                                WriteFrameWithDupes(dupesAmount, fileContent, lastNum, ext, debug, $"[In: {frameName}] [DISCARDED]");
                            }
                        }
                        else
                        {
                            for (int dupeCount = 1; dupeCount < interpFramesAmount; dupeCount++)
                            {
                                totalFileCount++;
                                WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [BLEND FRAME]");
                            }
                        }

                        frm = interpFramesAmount;
                    }
                    else
                    {
                        totalFileCount++;
                        WriteFrameWithDupes(dupesAmount, fileContent, totalFileCount, ext, debug, $"[In: {frameName}] [{((frm == 0) ? " Source " : $"Interp {frm}")}]");
                    }
                }
                // Keep same number of elements as interp frames
                for (int j = savedTotalFileCount; j < totalFileCount; j++)
                    currentinputFilenames.Add(frameFiles[i].Name);
                savedTotalFileCount = totalFileCount;
            }

            frameFileContents[number] = fileContent.ToString();
        }

        static void WriteFrameWithDupes(int dupesAmount, StringBuilder fileContent, int frameNum, string ext, bool debug, string debugNote = "", string forcedNote = "")
        {
            string fileName = frameNum.ToString().PadLeft(Padding.interpFrames, '0') + ext;
            for (int writtenDupes = -1; writtenDupes < dupesAmount; writtenDupes++)      // Write duplicates
            {
                string debugStr = debug ? (writtenDupes != -1 ? $"Dupe {writtenDupes + 1:000} " : string.Empty) + $"{debugNote} " : string.Empty;
                fileContent.Append($"file '{Paths.interpDir}/{fileName}' # {debugStr} {forcedNote}\n");
                fileContent.Append(durationLine);
                if (Interpolate.currentSettings.is3D)
                {
                    fileContent.Append($"file '{Paths.GetOtherDir(Paths.interpDir)}/{fileName}' # {debugStr}{forcedNote}\n");
                    fileContent.Append(durationLine);
                }
            }
        }
    }
}
