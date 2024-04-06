using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Flowframes.Ui
{
    public static class ControlExtensions
    {
        internal static class NativeMethods
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool LockWindowUpdate(IntPtr hWndLock);

            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
            internal const int WM_SETREDRAW = 0x0b;
        }

        public static void Suspend(this Control control)
        {
            NativeMethods.LockWindowUpdate(control.Handle);
        }

        public static void Resume(this Control control)
        {
            NativeMethods.LockWindowUpdate(IntPtr.Zero);
        }

        public static void SuspendDrawing(this Control control)
        {
            NativeMethods.SendMessage(control.Handle, NativeMethods.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ResumeDrawing(this Control control)
        {
            NativeMethods.SendMessage(control.Handle, NativeMethods.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            //control.Refresh();
        }

        public static void InvokeSafe(this Control control, Action action, bool asynch = false)
        {
            if (control.InvokeRequired)
                if (asynch)
                    control.BeginInvoke(action);
                else
                    control.Invoke(action);
            else
                action();
        }

        public static List<Control> GetControls(this Control control)
        {
            List<Control> list = new List<Control>();
            var controls = control.Controls.Cast<Control>().ToList();
            list.AddRange(controls);
            controls.ForEach(c => list.AddRange(c.GetControls()));
            return list;
        }
    }
}
