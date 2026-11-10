using System;
using System.IO;

namespace Ultragear.Core
{
    public static class Log
    {
        static readonly object Gate = new();
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ultragear");
        public static string FilePath => Path.Combine(Folder, "ultragear.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch { /* logging must never break the app */ }
        }
    }
}
