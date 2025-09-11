using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalUtilities
{
    public static class Log
    {
        static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        static readonly Task writeTask = default;
        static readonly ManualResetEvent pause = new ManualResetEvent(false);
        static string logPath;
        static Log()
        {
            writeTask = new Task((object obj) =>
            {
                while (true)
                {
                    pause.WaitOne();
                    pause.Reset();
                    List<string> temp = new List<string>();
                    foreach (string str in logQueue)
                    {
                        temp.Add(str);
                        logQueue.TryDequeue(out string val);
                    }
                    foreach (string str in temp)
                    {
                        WriteText(str);
                    }
                }
            }, null, TaskCreationOptions.LongRunning);
            writeTask.Start();
        }
        public static void WriteLog(string infoData, string source = "", string preText = "")
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + ":" + infoData);
            WriteLog(string.Empty, preText, infoData, source);
        }
        public static void WriteLog(string customDirectory, string preText, string infoData, string source)
        {
            logPath = GetLogPath(customDirectory, preText);
            string logContent = string.Concat("[", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), "]", source, ": ", infoData);
            logQueue.Enqueue(logContent);
            pause.Set();
        }
        private static string GetLogPath(string customDirectory, string preText)
        {
            string newFilePath = string.Empty;
            string logDir = string.IsNullOrEmpty(customDirectory) ? Path.Combine(Environment.CurrentDirectory, "logs") : customDirectory;
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            string extension = ".log";
            string fileNameNotExt = preText + "_" + DateTime.Now.ToString("yyyyMMdd");
            string fileName = string.Concat(fileNameNotExt, extension);
            string fileNamePattern = string.Concat(fileNameNotExt, "(*)", extension);
            List<string> filePaths = Directory.GetFiles(logDir, fileNamePattern, SearchOption.TopDirectoryOnly).ToList();

            if (filePaths.Count > 0)
            {
                int fileMaxLen = filePaths.Max(d => d.Length);
                string lastFilePath = filePaths.Where(d => d.Length == fileMaxLen).OrderByDescending(d => d).FirstOrDefault();
                if (new FileInfo(lastFilePath).Length > 1 * 1024 * 1024)
                {
                    string no = new Regex(@"(?is)(?<=\()(.*)(?=\))").Match(Path.GetFileName(lastFilePath)).Value;
                    bool parse = int.TryParse(no, out int tempno);
                    string formatno = string.Format("({0})", parse ? (tempno + 1) : tempno);
                    string newFileName = string.Concat(fileNameNotExt, formatno, extension);
                    newFilePath = Path.Combine(logDir, newFileName);
                }
                else
                {
                    newFilePath = lastFilePath;
                }
            }
            else
            {
                string newFileName = string.Concat(fileNameNotExt, string.Format("({0})", 0), extension);
                newFilePath = Path.Combine(logDir, newFileName);
            }
            return newFilePath;
        }
        private static void WriteText(string logContent)
        {
            using (FileStream fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                using (StreamWriter sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    sw.Write(logContent + "\r\n");
                    sw.Flush();
                    sw.Close();
                }
            }
            //Console.WriteLine(logContent);
        }
    }
}
