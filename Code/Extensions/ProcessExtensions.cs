using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Flowframes.Extensions
{
    public static class ProcessExtensions
    {
        internal static class NativeMethods
        {
            [Flags]
            public enum ThreadAccess : int
            {
                TERMINATE = (0x0001),
                SUSPEND_RESUME = (0x0002),
                GET_CONTEXT = (0x0008),
                SET_CONTEXT = (0x0010),
                SET_INFORMATION = (0x0020),
                QUERY_INFORMATION = (0x0040),
                SET_THREAD_TOKEN = (0x0080),
                IMPERSONATE = (0x0100),
                DIRECT_IMPERSONATION = (0x0200)
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern SafeThreadHandle OpenThread(ThreadAccess dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint ResumeThread(SafeThreadHandle hThread);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SuspendThread(SafeThreadHandle hThread);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr hObject);
        }

        internal class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            protected SafeThreadHandle() : base(true)
            {
            }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            override protected bool ReleaseHandle()
            {
                return NativeMethods.CloseHandle(handle);
            }
        }

        public static void Suspend(this Process process)
        {
            foreach (ProcessThread thread in process.Threads)
            {
                SafeThreadHandle pOpenThread = NativeMethods.OpenThread(NativeMethods.ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                if (!pOpenThread.IsInvalid)
                    _ = NativeMethods.SuspendThread(pOpenThread);
            }
        }

        public static void Resume(this Process process)
        {
            foreach (ProcessThread thread in process.Threads)
            {
                SafeThreadHandle pOpenThread = NativeMethods.OpenThread(NativeMethods.ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                if (!pOpenThread.IsInvalid)
                    _ = NativeMethods.ResumeThread(pOpenThread);
            }
        }

        public static async Task<int> WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Process_Exited(object sender, EventArgs e)
            {
                tcs.TrySetResult(process.ExitCode);
            }

            try
            {
                process.EnableRaisingEvents = true;
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // This is expected when trying to enable events after the process has already exited.
                // Simply ignore this case.
                // Allow the exception to bubble in all other cases.
            }

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                process.Exited += Process_Exited;

                try
                {

                    if (process.HasExited)
                    {
                        tcs.TrySetResult(process.ExitCode);
                    }

                    return await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    process.Exited -= Process_Exited;
                }
            }
        }
    }
}
