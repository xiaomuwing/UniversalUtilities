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
        private struct LogMessage
        {
            public string Directory;
            public string PreText;
            public string Content;
        }
        private static readonly BlockingCollection<LogMessage> myLogQueue = new();        // 使用阻塞集合处理并发
        private static readonly Regex _noRegex = new(@"(?<=\()(\d+)(?=\))", RegexOptions.Compiled);        // 预编译正则
        private const long MAX_FILE_SIZE = 1 * 1024 * 1024; // 1MB 阈值
        static Log()
        {
            Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning); // 开启后台消费任务
        }
        public static void WriteLog(string infoData, string source = "", string preText = "")
        {
            WriteLog(string.Empty, preText, infoData, source);
        }
        public static void WriteLog(string customDirectory, string preText, string infoData, string source)
        {
            string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]{source}: {infoData}"; // 封装日志上下文，避免全局变量竞争
            myLogQueue.Add(new LogMessage {Directory = customDirectory, PreText = preText, Content = logContent});
        }
        private static void ProcessQueue()
        {
            while (!myLogQueue.IsCompleted)
            {
                try
                {
                    if (myLogQueue.TryTake(out LogMessage firstMsg, Timeout.Infinite)) // 阻塞式获取第一条数据
                    {
                        var batch = new List<LogMessage> { firstMsg };
                        while (batch.Count < 1000 && myLogQueue.TryTake(out LogMessage nextMsg)) // 贪婪聚合，单次批处理上限 1000 条
                        {
                            batch.Add(nextMsg);
                        }
                        foreach (var group in batch.GroupBy(m => new { m.Directory, m.PreText })) // 按路径分组批量写入
                        {
                            WriteBatchSafe(group.Key.Directory, group.Key.PreText, group.Select(m => m.Content));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"写入异常: {ex.Message}");
                }
            }
        }
        private static void WriteBatchSafe(string customDir, string preText, IEnumerable<string> lines)
        {
            string currentPath = GetLogPath(customDir, preText);
            using var fs = new FileStream(currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            foreach (var line in lines)
            {
                sw.WriteLine(line);
                // 每写入一条，简单估算。若大批量写入导致超限，则在此关闭并切换文件。
                // 为了性能不建议每行都 Check FileInfo，采用写完一批后由下次 GetLogPath 修正
            }
        }
        private static string GetLogPath(string customDirectory, string preText)
        {
            string logDir = string.IsNullOrEmpty(customDirectory) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs") : customDirectory; // 确定基础目录
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            string fileNameNotExt = $"{preText}_{dateStr}";
            string extension = ".log";
            var files = Directory.GetFiles(logDir, fileNameNotExt + "(*)" + extension); // 获取该类型的所有分片文件
            if (files.Length == 0)
            {
                return Path.Combine(logDir, $"{fileNameNotExt}(0){extension}");
            }
            var latestFile = files   // 解析编号 (n) 进行排序
                .Select(f =>
                {
                    var match = _noRegex.Match(Path.GetFileName(f));
                    return new { Path = f, No = match.Success ? int.Parse(match.Value) : -1 };
                })
                .OrderByDescending(x => x.No)
                .First(); 
            if (new FileInfo(latestFile.Path).Length >= MAX_FILE_SIZE)     // 检查该分片是否已满 
            {
                return Path.Combine(logDir, $"{fileNameNotExt}({latestFile.No + 1}){extension}");
            }
            return latestFile.Path;
        }
    }
}
