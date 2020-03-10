﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Linq;

namespace PixelPlanetUtils.Logging
{

    using ConsoleLogEntry = ValueTuple<string, ConsoleColor>;
    using LogEntry = ValueTuple<string, MessageGroup, DateTime>;

    public class Logger : IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> messages = new ConcurrentQueue<LogEntry>();
        private ConcurrentQueue<ConsoleLogEntry> incomingConsoleMessages = new ConcurrentQueue<ConsoleLogEntry>();
        private ConcurrentQueue<ConsoleLogEntry> printableConsoleMessages;
        private readonly AutoResetEvent messagesAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent consoleMessagesAvailable = new AutoResetEvent(false);
        private readonly AutoResetEvent noPrintableMessages = new AutoResetEvent(true);
        private readonly StreamWriter logFileWriter;
        private readonly CancellationToken finishToken;
        private readonly object lockObj = new object();
        private bool disposed;
        private bool consolePaused = false;
        private readonly Thread loggingThread, consoleThread;

        public bool ShowDebugLogs { get; set; } = false;

        public string LogFilePath { get; }

        public Logger(CancellationToken finishToken) : this(null, finishToken)
        { }

        public Logger(string logFilePath, CancellationToken finishToken)
        {
            printableConsoleMessages = incomingConsoleMessages;
            this.finishToken = finishToken;
            loggingThread = new Thread(LogWriterThreadBody);
            loggingThread.Start();
            consoleThread = new Thread(ConsoleWriterThreadBody);
            consoleThread.Start();
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                logFilePath = Path.Combine(PathTo.LogsFolder,
                                            Assembly.GetEntryAssembly().GetName().Name,
                                            $"{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}_{Guid.NewGuid().ToString("N")}.log");
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFilePath)));
                logFileWriter = new StreamWriter(logFilePath, true);
                LogFilePath = logFilePath;
            }
            catch
            {
                throw new Exception("Cannot init file logging");
            }
        }

        public void LogAndPause(string msg, MessageGroup group)
        {
            if (!consolePaused)
            {
                string text;
                lock (lockObj)
                {
                    text = FormatLine(msg, group, DateTime.Now);
                    logFileWriter.WriteLine(text);
                    consolePaused = true;
                    incomingConsoleMessages = new ConcurrentQueue<ConsoleLogEntry>();
                }
                noPrintableMessages.Reset();
                printableConsoleMessages.Enqueue((text, ColorOf(group)));
                consoleMessagesAvailable.Set();
                noPrintableMessages.WaitOne();
            }
            else
            {
                throw new InvalidOperationException("Already paused");
            }
        }

        public void ResumeLogging()
        {
            if (consolePaused)
            {
                lock (lockObj)
                {
                    consolePaused = false;
                    printableConsoleMessages = incomingConsoleMessages;
                }
                consoleMessagesAvailable.Set();
            }
        }

        private static ConsoleColor ColorOf(MessageGroup group)
        {
            switch (group)
            {
                case MessageGroup.Attack:
                case MessageGroup.Captcha:
                case MessageGroup.PixelFail:
                case MessageGroup.Error:
                    return ConsoleColor.Red;
                case MessageGroup.Assist:
                case MessageGroup.Pixel:
                    return ConsoleColor.Green;
                case MessageGroup.Info:
                    return ConsoleColor.Magenta;
                case MessageGroup.TechInfo:
                    return ConsoleColor.Cyan;
                case MessageGroup.TechState:
                    return ConsoleColor.Yellow;
                case MessageGroup.PixelInfo:
                case MessageGroup.Debug:
                default:
                    return ConsoleColor.DarkGray;
            }
        }

        private readonly static int padLength = 2 + //brackets
            Enum.GetValues(typeof(MessageGroup)).Cast<MessageGroup>().Max(mg => mg.ToString().Length);

        private static readonly string space = new string(' ', 2);
        private static readonly string largeSpace = new string(' ', 5);

        private static string FormatLine(string msg, MessageGroup group, DateTime time)
        {
            if (group == MessageGroup.Debug)
            {
                return string.Concat(time.ToString("HH:mm:ss.fff"), space,
                                        $"[{group.ToString().ToUpper()}]", space,
                                        msg);
            }
            else
            {
                return string.Concat(time.ToString("HH:mm:ss.fff"), space,
                                        $"[{group.ToString().ToUpper()}]".PadRight(padLength), largeSpace,
                                        msg);
            }
        }

        public void Log(string msg, MessageGroup group, DateTime time)
        {
            messages.Enqueue((msg, group, time));
            messagesAvailable.Set();
        }

        private void LogWriterThreadBody()
        {
            while (true)
            {
                if (messages.TryDequeue(out LogEntry entry))
                {
                    lock (lockObj)
                    {
                        (string msg, MessageGroup group, DateTime time) = entry;
                        string line = FormatLine(msg, group, time);
                        logFileWriter.WriteLine(line);
                        if (ShowDebugLogs || group != MessageGroup.Debug)
                        {
                            incomingConsoleMessages.Enqueue((line, ColorOf(group)));
                            consoleMessagesAvailable.Set();
                        }
                    }
                }
                else
                {
                    if (disposed || finishToken.IsCancellationRequested)
                    {
                        return;
                    }
                    messagesAvailable.WaitOne();
                }
            }
        }

        private void ConsoleWriterThreadBody()
        {
            while (true)
            {
                if (printableConsoleMessages.TryDequeue(out ConsoleLogEntry msg))
                {
                    (string line, ConsoleColor color) = msg;
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                }
                else
                {
                    noPrintableMessages.Set();
                    if (disposed || finishToken.IsCancellationRequested)
                    {
                        return;
                    }
                    consoleMessagesAvailable.WaitOne();
                }
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                messagesAvailable.Set();
                consoleMessagesAvailable.Set();
                noPrintableMessages.Set();
                Thread.Sleep(50);
                logFileWriter.Close();
                messagesAvailable.Dispose();
                consoleMessagesAvailable.Dispose();
                noPrintableMessages.Dispose();
            }
        }

        static Logger()
        {
            ClearOldLogs();
        }

        private static void ClearOldLogs()
        {
            TimeSpan maxLogAge = TimeSpan.FromDays(7);
            DirectoryInfo di = new DirectoryInfo(PathTo.AppFolder);
            foreach (FileInfo logFile in di.EnumerateFiles("*.log")
                                           .Where(logFile => DateTime.Now - logFile.LastWriteTime > maxLogAge))
            {
                logFile.Delete();
            }
        }
    }

}
