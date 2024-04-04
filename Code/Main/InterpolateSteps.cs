﻿using Flowframes.IO;
using Flowframes.MiscUtils;
using Flowframes.Ui;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Flowframes.Main
{
    using static Interpolate;

    class InterpolateSteps
    {

        public static async Task Run(string step)
        {
            Logger.Log($"[SBS] Running step '{step}'", true);
            canceled = false;
            Program.mainForm.SetWorking(true);

            if (currentSettings == null)
            {
                Logger.Log($"[SBS] Getting new current settings", true);
                currentSettings = Program.mainForm.GetCurrentSettings();
            }
            else
            {
                Logger.Log($"[SBS] Updating current settings", true);
                currentSettings = Program.mainForm.UpdateCurrentSettings(currentSettings);
            }

            currentSettings.RefreshAlpha();
            currentSettings.stepByStep = true;

            if (!InterpolateUtils.InputIsValid(currentSettings)) return;     // General input checks
            if (!InterpolateUtils.CheckPathValid(currentSettings.inPath)) return;           // Check if input path/file is valid

            if (step.Contains("Extract Frames"))
                await ExtractFramesStep();

            if (step.Contains("Run Interpolation"))
                await InterpolateStep();

            if (step.Contains("Export"))
                await CreateOutputVid();

            if (step.Contains("Cleanup"))
                await Reset();

            Program.mainForm.SetWorking(false);
            Program.mainForm.SetStatus("Done running step.");
            Logger.Log("Done running this step.");
        }

        public static async Task ExtractFramesStep()
        {
            if (!(await IoUtils.TryDeleteIfExistsAsync(currentSettings.framesFolder)))
            {
                UiUtils.ShowMessageBox("Failed to delete existing frames folder - Make sure no file is opened in another program!", UiUtils.MessageType.Error);
                return;
            }

            await GetFrames();
            await PostProcessFrames();
        }

        public static async Task InterpolateStep()
        {
            if (!InterpolateUtils.CheckAiAvailable(currentSettings.ai, currentSettings.model)) return;

            currentSettings.framesFolder = Path.Combine(currentSettings.tempFolder, Paths.framesDir);

            currentSettings.RefreshExtensions();

            bool skip = await AutoEncodeResume.PrepareResumedRun();
            if (skip || canceled) return;

            if (IoUtils.GetAmountOfFiles(currentSettings.framesFolder, false, "*") < 2)
            {
                if (Config.GetBool(Config.Key.sbsRunPreviousStepIfNeeded))
                {
                    Logger.Log($"There are no extracted frames to interpolate - Running extract step first...");
                    await ExtractFramesStep();
                }

                if (IoUtils.GetAmountOfFiles(currentSettings.framesFolder, false, "*") < 2)
                {
                    UiUtils.ShowMessageBox("There are no extracted frames that can be interpolated!\nDid you run the extraction step?", UiUtils.MessageType.Error);
                    return;
                }
            }

            if (!await IoUtils.TryDeleteIfExistsAsync(currentSettings.interpFolder))
            {
                UiUtils.ShowMessageBox("Failed to delete existing interpolated frames folder - Make sure no file is opened in another program!", UiUtils.MessageType.Error);
                return;
            }

            if (!await IoUtils.TryDeleteIfExistsAsync(Paths.GetOtherDir(currentSettings.interpFolder)))
            {
                UiUtils.ShowMessageBox("Failed to delete existing other interpolated frames folder - Make sure no file is opened in another program!", UiUtils.MessageType.Error);
                return;
            }

            if (Config.GetBool(Config.Key.sbsAllowAutoEnc) && !(await InterpolateUtils.CheckEncoderValid())) return;

            if (canceled) return;

            Program.mainForm.SetStatus("Running AI...");
            await RunAi(currentSettings.interpFolder, currentSettings.ai, true);
            await FrameRename.UnRename(); // Get timestamps back
            Program.mainForm.SetProgress(0);
        }

        public static async Task CreateOutputVid()
        {
            if (IoUtils.GetAmountOfFiles(currentSettings.interpFolder, false) < 2)
            {
                if (Config.GetBool(Config.Key.sbsRunPreviousStepIfNeeded))
                {
                    Logger.Log($"There are no interpolated frames to export - Running interpolation step first...");
                    await InterpolateStep();
                }

                if (IoUtils.GetAmountOfFiles(currentSettings.interpFolder, false) < 2)
                {
                    Cancel($"There are no interpolated frames to encode!\n\nDid you delete the folder?");
                    return;
                }
            }

            if (!(await InterpolateUtils.CheckEncoderValid())) return;

            string[] outFrames = IoUtils.GetFilesSorted(currentSettings.interpFolder, "*" + currentSettings.interpExt);

            if (outFrames.Length > 0 && !IoUtils.CheckImageValid(outFrames[0]))
            {
                UiUtils.ShowMessageBox("Invalid frame files detected!\n\nIf you used Auto-Encode, this is normal, and you don't need to run " +
                    "this step as the video was already created in the \"Interpolate\" step.", UiUtils.MessageType.Error);
                return;
            }

            await Export.ExportFrames(currentSettings.interpFolder, currentSettings.outPath, currentSettings.outSettings, true);
        }

        public static async Task Reset()
        {
            DialogResult dialog = UiUtils.ShowMessageBox($"Are you sure you want to remove all temporary files?", "Are you sure?", MessageBoxButtons.YesNo);

            if (dialog == DialogResult.Yes)
                await Cleanup(true);
        }
    }
}
