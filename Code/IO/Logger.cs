using Flowframes.IO;
using Flowframes.Ui;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Flowframes
{
    class Logger
    {
        public static TextBox textbox;
        public const string defaultLogName = "sessionlog";
        private static long id;

        private static readonly ConcurrentDictionary<string, ConcurrentQueueL<string>> sessionLogs = new ConcurrentDictionary<string, ConcurrentQueueL<string>>();
        private static string _lastUi = "";
        public static string LastUiLine { get { return _lastUi; } }
        private static string _lastLog = "";
        public static string LastLogLine { get { return _lastLog; } }

        class ConcurrentQueueL<T> : ConcurrentQueue<T>
        {
            public T Last { get; private set; }

            public new void Enqueue(T item)
            {
                Last = item;
                base.Enqueue(item);
            }
        }

        public struct LogEntry
        {
            public string logMessage;
            public bool hidden;
            public bool replaceLastLine;
            public string filename;
            public DateTime time;

            public LogEntry(string logMessageArg, bool hiddenArg = false, bool replaceLastLineArg = false, string filenameArg = "")
            {
                logMessage = logMessageArg;
                hidden = hiddenArg;
                replaceLastLine = replaceLastLineArg;
                filename = filenameArg;
                time = DateTime.Now;
            }
        }

        private static readonly BlockingCollection<LogEntry> logQueue = new BlockingCollection<LogEntry>();

        public static void Log(string msg, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            logQueue.Add(new LogEntry(msg, hidden, replaceLastLine, filename));
        }

        public static void StartLogging()
        {
            Task.Run(() =>
            {
                Console.WriteLine("Start logging");
                try
                {
                    while (true)
                        Show(logQueue.Take());
                }
                catch (InvalidOperationException)
                {
                    logQueue.Dispose();
                    Console.WriteLine("Finished logging");
                }
            });
        }

        public static void StopLogging()
        {
            logQueue.CompleteAdding();
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
                    textbox.Text = string.Join(Environment.NewLine, lines.Take(lines.Length - 1).ToArray());
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

            LogToFile(entry.time, msg, false, entry.filename);
        }

        private static void LogToFile(DateTime time, string logStr, bool noLineBreak, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                filename = defaultLogName;

            if (Path.GetExtension(filename) != ".txt")
                filename = Path.ChangeExtension(filename, "txt");

            string file = Path.Combine(Paths.GetLogPath(), filename);
            logStr = logStr.Replace(Environment.NewLine, " ").TrimWhitespaces();

            try
            {
                string appendStr = noLineBreak ? $" {logStr}" : $"{Environment.NewLine}[{id.ToString().PadLeft(8, '0')}] [{time:yyyy-MM-dd HH:mm:ss}]: {logStr}";

                if (sessionLogs.TryGetValue(filename, out ConcurrentQueueL<string> sessionLog))
                {
                    if (sessionLog.Count > 9)
                        sessionLog.TryDequeue(out _);
                }
                else
                {
                    sessionLog = new ConcurrentQueueL<string>();
                    sessionLogs[filename] = sessionLog;
                }
                sessionLog.Enqueue(appendStr);

                File.AppendAllText(file, appendStr);
                id++;
            }
            catch
            {
                // this if fine, i forgot why
            }
        }

        private static ConcurrentQueueL<string> GetSessionLog(string filename)
        {
            if (!filename.Contains(".txt"))
                filename = Path.ChangeExtension(filename, "txt");

            sessionLogs.TryGetValue(filename, out ConcurrentQueueL<string> logQ);
            return logQ;
        }

        public static List<string> GetSessionLogLastLines(string filename, int linesCount)
        {
            ConcurrentQueueL<string> logQ = GetSessionLog(filename);
            List<string> log = logQ != null ? logQ.ToList() : new List<string>();
            return log.Count > linesCount ? log.GetRange(log.Count - linesCount, linesCount) : log;
        }

        public static string GetSessionLogLastLine(string filename)
        {
            ConcurrentQueueL<string> logQ = GetSessionLog(filename);
            return logQ?.Last;
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

            string file = Path.Combine(Paths.GetLogPath(), filename);

            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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
