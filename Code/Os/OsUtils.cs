﻿using Flowframes.Extensions;
using Flowframes.IO;
using Flowframes.MiscUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tulpep.NotificationWindow;

namespace Flowframes.Os
{
    class OsUtils
    {
        public static string GetProcStdOut(Process proc, bool includeStdErr = false, ProcessPriorityClass priority = ProcessPriorityClass.BelowNormal)
        {
            if (includeStdErr && !proc.StartInfo.Arguments.EndsWith("2>&1"))
                proc.StartInfo.Arguments += " 2>&1";

            proc.Start();
            proc.PriorityClass = priority;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output;
        }

        public static bool IsUserAdministrator()
        {
            //bool value to hold our return value
            bool isAdmin;
            WindowsIdentity user = null;
            try
            {
                //get the currently logged in user
                user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception e)
            {
                Logger.Log("IsUserAdministrator() Error: " + e.Message);
                isAdmin = false;
            }
            finally
            {
                user?.Dispose();
            }
            return isAdmin;
        }

        public static Process SetStartInfo(Process proc, bool hidden, string filename = "cmd.exe")
        {
            proc.StartInfo.UseShellExecute = !hidden;
            proc.StartInfo.RedirectStandardOutput = hidden;
            proc.StartInfo.RedirectStandardError = hidden;
            proc.StartInfo.CreateNoWindow = hidden;
            proc.StartInfo.FileName = filename;
            return proc;
        }

        public static bool IsProcessHidden(Process proc)
        {
            bool defaultVal = true;

            try
            {
                if (proc == null)
                {
                    Logger.Log($"IsProcessHidden was called but proc is null, defaulting to {defaultVal}", true);
                    return defaultVal;
                }

                if (proc.HasExited)
                {
                    Logger.Log($"IsProcessHidden was called but proc has already exited, defaulting to {defaultVal}", true);
                    return defaultVal;
                }

                ProcessStartInfo si = proc.StartInfo;
                return !si.UseShellExecute && si.CreateNoWindow;
            }
            catch (Exception e)
            {
                Logger.Log($"IsProcessHidden errored, defaulting to {defaultVal}: {e.Message}", true);
                return defaultVal;
            }
        }

        public static Process NewProcess(bool hidden, string filename = "cmd.exe")
        {
            Process proc = new Process();
            return SetStartInfo(proc, hidden, filename);
        }

        public static void KillProcessTree(int pid)
        {
            // Check if the process with the given pid is running
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc?.Kill();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            // Query to find child processes
            ManagementObjectSearcher processSearcher = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={pid}");
            ManagementObjectCollection processCollection = processSearcher.Get();

            if (processCollection != null)
            {
                foreach (ManagementObject mo in processCollection.Cast<ManagementObject>())
                {
                    // Recursively kill child processes
                    KillProcessTree(Convert.ToInt32(mo["ProcessID"]));
                }
            }
        }

        public static string GetCmdArg()
        {
            bool stayOpen = Config.GetInt(Config.Key.cmdDebugMode) == 2;
            string path = $"set path={Path.Combine(Paths.GetPkgPath(), Paths.audioVideoDir)};%path% &";

            if (stayOpen)
                return "/K " + path;
            else
                return "/C " + path;
        }

        public static bool ShowHiddenCmd()
        {
            return Config.GetInt(Config.Key.cmdDebugMode) > 0;
        }

        public static bool DriveIsSSD(string path)
        {
            return true;
        }

        public static bool HasNonAsciiChars(string str)
        {
            return (Encoding.UTF8.GetByteCount(str) != str.Length);
        }

        //public static int GetFreeRamMb()
        //{
        //    try
        //    {
        //        return (int)(new ComputerInfo().AvailablePhysicalMemory / 1048576);
        //    }
        //    catch
        //    {
        //        return 1000;
        //    }
        //}

        public static string TryGetOs()
        {
            string info = string.Empty;

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    ManagementObjectCollection information = searcher.Get();

                    if (information != null)
                    {
                        foreach (ManagementObject obj in information.Cast<ManagementObject>())
                            info = $"{obj["Caption"]} | {obj["OSArchitecture"]}";
                    }

                    info = info.Replace("NT 5.1.2600", "XP").Replace("NT 5.2.3790", "Server 2003");
                }
            }
            catch (Exception e)
            {
                Logger.Log("TryGetOs Error: " + e.Message, true);
            }

            return info;
        }

        public static IEnumerable<Process> GetChildProcesses(Process process)
        {
            List<Process> children = new List<Process>();
            ManagementObjectSearcher mos = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", process.Id));

            foreach (ManagementObject mo in mos.Get().Cast<ManagementObject>())
            {
                children.Add(Process.GetProcessById(Convert.ToInt32(mo["ProcessID"])));
            }

            return children;
        }

        public static async Task<string> GetOutputAsync(Process process, bool onlyLastLine = false)
        {
            Logger.Log($"Getting output for {process.StartInfo.FileName} {process.StartInfo.Arguments}", true);
            NmkdStopwatch sw = new NmkdStopwatch();

            string output = string.Empty;

            TaskCompletionSource<object> outputTcs = new TaskCompletionSource<object>(), errorTcs = new TaskCompletionSource<object>();
            process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => { if (e.Data == null) outputTcs.SetResult(null); else output += $"{e.Data}\n"; };
            process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => { if (e.Data == null) errorTcs.SetResult(null); else output += $"{e.Data}\n"; };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.WhenAll(process.WaitForExitAsync(), outputTcs.Task, errorTcs.Task);

            output = output.Trim('\r', '\n');

            Logger.Log($"Output (after {sw}):  {output.Replace("\r", " / ").Replace("\n", " / ").Trunc(250)}", true);

            if (onlyLastLine)
                output = output.SplitIntoLines().LastOrDefault();

            return output;
        }

        public static void Shutdown()
        {
            Process proc = NewProcess(true);
            proc.StartInfo.Arguments = "/C shutdown -s -t 0";
            proc.Start();
        }

        public static void Hibernate()
        {
            Application.SetSuspendState(PowerState.Hibernate, true, true);
        }

        public static void Sleep()
        {
            Application.SetSuspendState(PowerState.Suspend, true, true);
        }

        public static void ShowNotification(string title, string text)
        {
            var popupNotifier = new PopupNotifier { TitleText = title, ContentText = text, IsRightToLeft = false };
            popupNotifier.BodyColor = System.Drawing.ColorTranslator.FromHtml("#323232");
            popupNotifier.ContentColor = System.Drawing.Color.White;
            popupNotifier.TitleColor = System.Drawing.Color.LightGray;
            popupNotifier.GradientPower = 0;
            popupNotifier.Popup();
        }

        public static void ShowNotificationIfInBackground(string title, string text)
        {
            if (Program.mainForm.IsInFocus())
                return;

            ShowNotification(title, text);
        }

        public static string GetGpus()
        {
            List<string> gpusVk = new List<string>();
            List<string> gpusNv = new List<string>();

            if (VulkanUtils.VkDevices != null)
            {
                gpusVk.AddRange(VulkanUtils.VkDevices.Select(d => $"{d.Name.Remove("NVIDIA ").Remove("GeForce ").Remove("AMD ").Remove("Intel ").Remove("(TM)")} ({d.Id})"));
            }

            if (NvApi.gpuList != null && NvApi.gpuList.Count != 0)
            {
                gpusNv.AddRange(NvApi.gpuList.Select(d => $"{d.FullName.Remove("NVIDIA ").Remove("GeForce ")} ({NvApi.gpuList.IndexOf(d)})"));
            }

            if (gpusVk.Count == 0 && gpusNv.Count == 0)
                return "No GPUs detected.";

            string s = string.Empty;

            if (gpusVk.Count != 0)
            {
                s += $"Vulkan GPUs: {string.Join(", ", gpusVk)}";
            }

            if (gpusNv.Count != 0)
            {
                s += $" - CUDA GPUs: {string.Join(", ", gpusNv)}";
            }

            return s;
        }

        public static string GetPathVar(string additionalPath = null)
        {
            return GetPathVar(new[] { additionalPath });
        }

        public static string GetPathVar(IEnumerable<string> additionalPaths)
        {
            var paths = Environment.GetEnvironmentVariable("PATH").Split(';');
            List<string> newPaths = new List<string>();

            if (paths != null)
                newPaths.AddRange(additionalPaths.Where(p => p.IsNotEmpty()));

            newPaths.AddRange(paths.Where(x => x.Lower().Replace("\\", "/").StartsWith("c:/windows")).ToList());

            return string.Join(";", newPaths.Select(x => x.Replace("\\", "/"))) + ";";
        }
    }
}