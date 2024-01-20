using Flowframes.IO;
using Flowframes.Ui;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DT = System.DateTime;

namespace Flowframes
{
    class Logger
    {
        public static TextBox textbox;
        static string file;
        public const string defaultLogName = "sessionlog";
        public static long id;

        private static ConcurrentDictionary<string, ConcurrentQueue<string>> sessionLogs = new ConcurrentDictionary<string, ConcurrentQueue<string>>();
        private static string _lastUi = "";
        public static string LastUiLine { get { return _lastUi; } }
        private static string _lastLog = "";
        public static string LastLogLine { get { return _lastLog; } }

        public struct LogEntry
        {
            public string logMessage;
            public bool hidden;
            public bool replaceLastLine;
            public string filename;

            public LogEntry(string logMessageArg, bool hiddenArg = false, bool replaceLastLineArg = false, string filenameArg = "")
            {
                logMessage = logMessageArg;
                hidden = hiddenArg;
                replaceLastLine = replaceLastLineArg;
                filename = filenameArg;
            }
        }

        private static ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();

        public static void Log(string msg, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            logQueue.Enqueue(new LogEntry(msg, hidden, replaceLastLine, filename));
            ShowNext();
        }

        private static void ShowNext()
        {
            LogEntry entry;

            if (logQueue.TryDequeue(out entry))
                Show(entry);
        }

        private static void Show(LogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.logMessage))
                return;

            string msg = entry.logMessage;

            if (msg == LastUiLine)
                entry.hidden = true; // Never show the same line twice in UI, but log it to file

            _lastLog = msg;

            if (!entry.hidden)
                _lastUi = msg;

            Console.WriteLine(msg);

            try
            {
                if (!entry.hidden && entry.replaceLastLine)
                {
                    textbox.Suspend();
                    string[] lines = textbox.Text.SplitIntoLines();
                    textbox.Text = string.Join(Environment.NewLine, lines.Take(lines.Count() - 1).ToArray());
                }
            }
            catch { }

            msg = msg.Replace("\n", Environment.NewLine);

            if (!entry.hidden && textbox != null)
                textbox.AppendText((textbox.Text.Length > 1 ? Environment.NewLine : "") + msg);

            if (entry.replaceLastLine)
            {
                textbox.Resume();
                msg = "[REPL] " + msg;
            }

            if (!entry.hidden)
                msg = "[UI] " + msg;

            LogToFile(msg, false, entry.filename);
        }

        private static void LogToFile(string logStr, bool noLineBreak, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                filename = defaultLogName;

            if (Path.GetExtension(filename) != ".txt")
                filename = Path.ChangeExtension(filename, "txt");

            file = Path.Combine(Paths.GetLogPath(), filename);
            logStr = logStr.Replace(Environment.NewLine, " ").TrimWhitespaces();
            string time = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss");

            try
            {
                string appendStr = noLineBreak ? $" {logStr}" : $"{Environment.NewLine}[{id.ToString().PadLeft(8, '0')}] [{time}]: {logStr}";

                if (sessionLogs.ContainsKey(filename))
                {
                    ConcurrentQueue<string> sessionLog = sessionLogs[filename];
                    sessionLog.Enqueue(appendStr);
                    if (sessionLog.Count > 10)
                        sessionLog.TryDequeue(out _);
                }
                else
                {
                    ConcurrentQueue<string> sessionLog = new ConcurrentQueue<string>();
                    sessionLogs[filename] = sessionLog;
                    sessionLog.Enqueue(appendStr);
                }

                File.AppendAllText(file, appendStr);
                id++;
            }
            catch
            {
                // this if fine, i forgot why
            }
        }

        private static ConcurrentQueue<string> GetSessionLog(string filename)
        {
            if (!filename.Contains(".txt"))
                filename = Path.ChangeExtension(filename, "txt");

            if (sessionLogs.ContainsKey(filename))
                return sessionLogs[filename];
            else
                return null;
        }

        public static List<string> GetSessionLogLastLines(string filename, int linesCount = 5)
        {
            ConcurrentQueue<string> logQ = GetSessionLog(filename);
            List<string> log = logQ != null ? logQ.ToList() : new List<string>();
            return log.Count > linesCount ? log.GetRange(0, linesCount) : log;
        }

        public static void LogIfLastLineDoesNotContainMsg(string s, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            if (!GetLastLine().Contains(s))
                Log(s, hidden, replaceLastLine, filename);
        }

        public static void WriteToFile(string content, bool append, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                filename = defaultLogName;

            if (Path.GetExtension(filename) != ".txt")
                filename = Path.ChangeExtension(filename, "txt");

            file = Path.Combine(Paths.GetLogPath(), filename);

            string time = DT.Now.Month + "-" + DT.Now.Day + "-" + DT.Now.Year + " " + DT.Now.Hour + ":" + DT.Now.Minute + ":" + DT.Now.Second;

            try
            {
                if (append)
                    File.AppendAllText(file, Environment.NewLine + time + ":" + Environment.NewLine + content);
                else
                    File.WriteAllText(file, Environment.NewLine + time + ":" + Environment.NewLine + content);
            }
            catch
            {

            }
        }

        public static void ClearLogBox()
        {
            textbox.Text = "";
        }

        public static string GetLastLine(bool includeHidden = false)
        {
            return includeHidden ? _lastLog : _lastUi;
        }

        public static void RemoveLastLine()
        {
            textbox.Text = textbox.Text.Remove(textbox.Text.LastIndexOf(Environment.NewLine));
        }
    }
}
