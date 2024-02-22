﻿using Flowframes.MiscUtils;
using System;
using System.Windows.Forms;

namespace Flowframes.Ui
{
    class QuickSettingsTab
    {
        public static bool trimEnabled;
        public static bool doTrimEnd;
        public static string trimStart;
        public static string trimEnd;
        public static long trimStartSecs;
        public static long trimEndSecs;

        public static void UpdateTrim(TextBox trimStartBox, TextBox trimEndBox)
        {
            trimStart = trimStartBox.Text.Trim();
            trimEnd = trimEndBox.Text.Trim();

            trimStartSecs = FormatUtils.TimestampToSecs(trimStart, false);
            trimEndSecs = FormatUtils.TimestampToSecs(trimEnd, false);

            if (trimEndSecs <= trimStartSecs)
                trimEndBox.Text = FormatUtils.SecsToTimestamp(trimStartSecs + 1);

            long dur = FormatUtils.TimestampToMs(trimEnd) - FormatUtils.TimestampToMs(trimStart);
            Program.mainForm.currInDurationCut = dur;

            doTrimEnd = FormatUtils.TimestampToMs(trimEnd) != FormatUtils.TimestampToMs(FormatUtils.MsToTimestamp(Program.mainForm.currInDuration));
        }

        public static string GetTrimEndMinusOne()
        {
            TimeSpan minusOne = TimeSpan.Parse(trimEnd).Subtract(new TimeSpan(0, 0, 1));
            Logger.Log($"returning {minusOne}", true, false, "ffmpeg");
            return minusOne.ToString();
        }
    }
}
