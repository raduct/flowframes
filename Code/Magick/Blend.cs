﻿using Flowframes.Data;
using Flowframes.IO;
using Flowframes.MiscUtils;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Paths = Flowframes.IO.Paths;

namespace Flowframes.Magick
{
    class Blend
    {
        public static async Task BlendSceneChanges(string framesFilePath, bool setStatus = true)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            int totalFrames = 0;

            string keyword = "SCN:";
            string fileContent = File.ReadAllText(framesFilePath);

            if (!fileContent.Contains(keyword))
            {
                Logger.Log("Skipping BlendSceneChanges as there are no scene changes in this frames file.", true);
                return;
            }

            string[] framesLines = fileContent.SplitIntoLines();     // Array with frame filenames

            string oldStatus = Program.mainForm.GetStatus();

            if (setStatus)
                Program.mainForm.SetStatus("Blending scene transitions...");

            string[] frames = FrameRename.framesAreRenamed ? new string[0] : IoUtils.GetFilesSorted(Interpolate.currentSettings.framesFolder);

            List<Task> runningTasks = new List<Task>();
            int maxThreads = Environment.ProcessorCount / 2;

            foreach (string line in framesLines)
            {
                try
                {
                    if (line.Contains(keyword))
                    {
                        string trimmedLine = line.Split(keyword).Last();
                        string interpFolder = line.Split('/').First().Remove("file '");
                        interpFolder = Path.Combine(Interpolate.currentSettings.tempFolder, interpFolder);
                        bool isOther = !interpFolder.Equals(Interpolate.currentSettings.interpFolder);
                        string framesFolder = !isOther ? Interpolate.currentSettings.framesFolder : Paths.GetOtherDir(Interpolate.currentSettings.framesFolder);
                        string[] values = trimmedLine.Split('>');
                        string frameFrom = FrameRename.framesAreRenamed ? values[0] : frames[values[0].GetInt()];
                        string frameTo = FrameRename.framesAreRenamed ? values[1] : frames[values[1].GetInt()];
                        int amountOfBlendFrames = values.Count() == 3 ? values[2].GetInt() : (int)Interpolate.currentSettings.interpFactor - 1;

                        string img1 = Path.Combine(framesFolder, frameFrom);
                        string img2 = Path.Combine(framesFolder, frameTo);

                        string firstOutputFrameName = line.Split('/').Last().Remove("'").Split('#').First();
                        string ext = Path.GetExtension(firstOutputFrameName);
                        int firstOutputFrameNum = firstOutputFrameName.GetInt();
                        List<string> outputFilenames = new List<string>();

                        for (int blendFrameNum = 1; blendFrameNum <= amountOfBlendFrames; blendFrameNum++)
                        {
                            int outputNum = firstOutputFrameNum + blendFrameNum;
                            string outputPath = Path.Combine(interpFolder, outputNum.ToString().PadLeft(Padding.interpFrames, '0'));
                            outputPath = Path.ChangeExtension(outputPath, ext);
                            outputFilenames.Add(outputPath);
                        }

                        while (runningTasks.Count >= maxThreads)
                        {
                            int x = Task.WaitAny(runningTasks.ToArray());
                            runningTasks.RemoveAt(x);
                        }

                        Logger.Log($"Starting task for transition {values[0]} > {values[1]} ({runningTasks.Count}/{maxThreads} running)", true);
                        
                        string[] outFilenames = outputFilenames.ToArray(); // capture variable
                        Task newTask = Task.Run(() => BlendImages(img1, img2, outFilenames));
                        runningTasks.Add(newTask);

                        totalFrames += outputFilenames.Count;
                    }
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to blend scene changes: {e.Message}\n{e.StackTrace}", true);
                }
            }

            await Task.WhenAll(runningTasks);

            Logger.Log($"Created {totalFrames} blend frames in {FormatUtils.TimeSw(sw)} ({(totalFrames / (sw.ElapsedMilliseconds / 1000f)).ToString("0.00")} FPS)", true);

            if (setStatus)
                Program.mainForm.SetStatus(oldStatus);
        }

        public static void BlendImages(string img1Path, string img2Path, string imgOutPath)
        {
            MagickImage img1 = new MagickImage(img1Path);
            MagickImage img2 = new MagickImage(img2Path);
            img2.Alpha(AlphaOption.Opaque);
            img2.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(50));
            img1.Composite(img2, Gravity.Center, CompositeOperator.Over);
            switch (Config.Get(Config.Key.interpFormat))
            {
                case "png":
                    img1.Format = MagickFormat.Png24;
                    break;
                case "jpg":
                    img1.Format = MagickFormat.Jpeg;
                    break;
                case "bmp":
                    img1.Format = MagickFormat.Bmp;
                    break;
                case "webp":
                    img1.Format = MagickFormat.WebP;
                    break;
                default:
                    img1.Format = MagickFormat.Png24;
                    break;
            }
            img1.Quality = 100;
            img1.Write(imgOutPath);
        }

        public static async Task BlendImages(string img1Path, string img2Path, string[] imgOutPaths)
        {
            try
            {
                using (MagickImage img1 = new MagickImage(img1Path), img2 = new MagickImage(img2Path))
                {
                    float alphaFraction = 100f / (imgOutPaths.Length + 1);   // Alpha percentage per image
                    float currentAlpha = alphaFraction;

                    await Task.CompletedTask;
                    foreach (string imgOutPath in imgOutPaths)
                    {
                        string outPath = imgOutPath.Trim();

                        using (MagickImage img1Inst = new MagickImage(img1), img2Inst = new MagickImage(img2))
                        {
                            img2Inst.Alpha(AlphaOption.Opaque);
                            img2Inst.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(currentAlpha));
                            currentAlpha += alphaFraction;

                            img1Inst.Composite(img2Inst, Gravity.Center, CompositeOperator.Over);

                            switch (Config.Get(Config.Key.interpFormat))
                            {
                                case "png":
                                    img1.Format = MagickFormat.Png24;
                                    break;
                                case "jpg":
                                    img1.Format = MagickFormat.Jpeg;
                                    break;
                                case "bmp":
                                    img1.Format = MagickFormat.Bmp;
                                    break;
                                case "webp":
                                    img1.Format = MagickFormat.WebP;
                                    break;
                                default:
                                    img1.Format = MagickFormat.Png24;
                                    break;
                            }
                            img1Inst.Quality = 100;
                            img1Inst.Write(outPath);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("BlendImages Error: " + e.Message);
            }
        }
    }
}
