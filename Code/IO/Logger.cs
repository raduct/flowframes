using Flowframes.IO;
using Flowframes.Ui;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Flowframes
{
    class Logger
    {
        private static TextBox textBox = null;
        public const string defaultLogName = "sessionlog";
        private static long gloabalId = 0;

        private static readonly ConcurrentDictionary<string, ConcurrentQueueL<string>> memLogs = new ConcurrentDictionary<string, ConcurrentQueueL<string>>(2, 3);
        private static string _lastUi = null;
        public static string LastUiLine { get { return _lastUi; } }

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
            public long id;

            public LogEntry(string logMessageArg, bool hiddenArg = false, bool replaceLastLineArg = false, string filenameArg = "")
            {
                logMessage = logMessageArg;
                hidden = hiddenArg;
                replaceLastLine = replaceLastLineArg;
                filename = filenameArg;
                id = Interlocked.Increment(ref gloabalId);
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

            WriteMemLog(logEntry); // Save into in-memory log

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
            entry.logMessage = entry.logMessage.Replace("\n", Environment.NewLine);

            if (entry.hidden) return;

            lock (lockShow)
            {
                if (entry.logMessage != LastUiLine) // Never show the same line twice in UI, but log it to file
                {
                    _lastUi = entry.logMessage;

                    WriteLogBox(entry.logMessage, entry.replaceLastLine);

                    entry.logMessage = "[UI] " + (entry.replaceLastLine ? "[REPL] " : string.Empty) + entry.logMessage;
                }
            }
        }

        private static void LogToFile(LogEntry entry)
        {
            string logStr = Environment.NewLine + entry.logMessage;
            string filePath = Path.Combine(Paths.GetLogPath(), entry.filename + ".txt");

            try
            {
                File.AppendAllText(filePath, logStr);
            }
            catch
            {
                // this if fine, i forgot why
            }
        }

        private static void WriteMemLog(LogEntry entry)
        {
            // Prepare for log
            if (string.IsNullOrWhiteSpace(entry.filename))
                entry.filename = defaultLogName;

            entry.logMessage = entry.logMessage.Replace(Environment.NewLine, " ").TrimWhitespaces();
            entry.logMessage = $"[{entry.id.ToString().PadLeft(8, '0')}] [{entry.time:yyyy-MM-dd HH:mm:ss}]: {entry.logMessage}";
            Console.WriteLine(entry.logMessage);

            if (memLogs.TryGetValue(entry.filename, out ConcurrentQueueL<string> memLog))
            {
                if (memLog.Count >= 10)
                    memLog.TryDequeue(out _);
            }
            else
            {
                memLog = new ConcurrentQueueL<string>();
                memLogs[entry.filename] = memLog;
            }
            memLog.Enqueue(entry.logMessage);
        }

        private static ConcurrentQueueL<string> GetMemLog(string filename)
        {
            memLogs.TryGetValue(filename, out ConcurrentQueueL<string> logQ);
            return logQ;
        }

        public static List<string> GetLogLastLines(string filename, int linesCount)
        {
            ConcurrentQueueL<string> logQ = GetMemLog(filename);
            List<string> log = logQ?.ToList() ?? new List<string>();
            return log.Count > linesCount ? log.GetRange(log.Count - linesCount, linesCount) : log;
        }

        public static string GetLogLastLine(string filename)
        {
            ConcurrentQueueL<string> logQ = GetMemLog(filename);
            return logQ?.Last;
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
            if (textBox == null || !textBox.IsHandleCreated)
                return;

            textBox.InvokeSafe(delegate
            {
                textBox.Clear();
            });
        }

        private static void WriteLogBox(string logMessage, bool replaceLastLine = false)
        {
            if (textBox == null || !textBox.IsHandleCreated)
                return;

            textBox.InvokeSafe(delegate
            {
                if (replaceLastLine)
                {
                    string text = textBox.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        int lastNL = text.LastIndexOf(Environment.NewLine);
                        text = (lastNL > 0 ? text.Substring(0, lastNL + Environment.NewLine.Length) : string.Empty) + logMessage;
                        textBox.SuspendDrawing();
                        textBox.Text = text;
                        textBox.ResumeDrawing();
                        textBox.SelectionStart = text.Length;
                        textBox.ScrollToCaret();
                        return;
                    }
                }
                textBox.AppendText((textBox.TextLength > 0 ? Environment.NewLine : string.Empty) + logMessage);
            }, true);
        }

        public static void SetLogBox(TextBox logBox)
        {
            textBox = logBox;
        }
    }
}
