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

        public class LogEntry
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

        private static readonly BlockingCollection<LogEntry> fileLogQueue = new BlockingCollection<LogEntry>();

        public static void Log(string msg, bool hidden = false, bool replaceLastLine = false, string filename = "")
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            LogEntry logEntry = new LogEntry(msg, hidden, replaceLastLine, filename);

            Show(logEntry); // Show entry synch

            WriteSessionLog(logEntry); // Save into in-memory log

            fileLogQueue.Add(logEntry); // Write to file asynch
        }

        public static void StartLogging()
        {
            Task.Run(() =>
            {
                Console.WriteLine("Start logging");
                try
                {
                    while (true)
                        LogToFile(fileLogQueue.Take());
                }
                catch (InvalidOperationException)
                {
                    fileLogQueue.Dispose();
                    Console.WriteLine("Finished logging");
                }
            });
        }

        public static void StopLogging()
        {
            fileLogQueue.CompleteAdding();
        }

        private static readonly object lockShow = new object();

        private static void Show(LogEntry entry)
        {
            lock (lockShow)
            {
                if (entry.logMessage == LastUiLine)
                    entry.hidden = true; // Never show the same line twice in UI, but log it to file

                _lastLog = entry.logMessage;

                if (!entry.hidden)
                    _lastUi = entry.logMessage;

                Console.WriteLine(entry.logMessage);

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

                entry.logMessage = entry.logMessage.Replace("\n", Environment.NewLine);

                if (!entry.hidden && textbox != null)
                    textbox.AppendText((textbox.Text.Length > 1 ? Environment.NewLine : "") + entry.logMessage);

                if (entry.replaceLastLine)
                {
                    textbox.Resume();
                    entry.logMessage = "[REPL] " + entry.logMessage;
                }

                if (!entry.hidden)
                    entry.logMessage = "[UI] " + entry.logMessage;
            }
        }

        private static void LogToFile(LogEntry entry)
        {
            LogToFile(entry.logMessage, entry.filename);
        }

        private static void LogToFile(string logStr, string filename)
        {
            string filePath = Path.Combine(Paths.GetLogPath(), filename);

            try
            {
                File.AppendAllText(filePath, logStr);
            }
            catch
            {
                // this if fine, i forgot why
            }
        }

        private static void WriteSessionLog(LogEntry entry)
        {
            // Prepare for log
            if (string.IsNullOrWhiteSpace(entry.filename))
                entry.filename = defaultLogName;
            if (Path.GetExtension(entry.filename) != ".txt")
                entry.filename = Path.ChangeExtension(entry.filename, "txt");

            entry.logMessage = entry.logMessage.Replace(Environment.NewLine, " ").TrimWhitespaces();
            entry.logMessage = $"{Environment.NewLine}[{id.ToString().PadLeft(8, '0')}] [{entry.time:yyyy-MM-dd HH:mm:ss}]: {entry.logMessage}";
            id++;

            if (sessionLogs.TryGetValue(entry.filename, out ConcurrentQueueL<string> sessionLog))
            {
                if (sessionLog.Count >= 10)
                    sessionLog.TryDequeue(out _);
            }
            else
            {
                sessionLog = new ConcurrentQueueL<string>();
                sessionLogs[entry.filename] = sessionLog;
            }
            sessionLog.Enqueue(entry.logMessage);
        }

        private static ConcurrentQueueL<string> GetSessionLog(string filename)
        {
            if (Path.GetExtension(filename) != ".txt")
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
