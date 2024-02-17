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
        }

        public static void Suspend(this Control control)
        {
            NativeMethods.LockWindowUpdate(control.Handle);
        }

        public static void Resume(this Control control)
        {
            NativeMethods.LockWindowUpdate(IntPtr.Zero);
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
