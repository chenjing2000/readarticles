using System.IO;
using ReadArticles.Infrastructure;

internal static class AppLoggerChecks
{
    internal static void Run()
    {
        Program.Run("runtime logs record diagnostics without exception messages or learning content", () =>
        {
            string root = NewDirectory();
            try
            {
                var logger = new AppLogger(root);
                logger.Warning("VocabularySaveFailed", new InvalidOperationException("PRIVATE_ARTICLE_TEXT_AND_VOCABULARY_MEANING"));
                string log = File.ReadAllText(Directory.GetFiles(root, "*.log").Single());
                Program.Check(log.Contains("VocabularySaveFailed", StringComparison.Ordinal));
                Program.Check(log.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
                Program.Check(!log.Contains("PRIVATE_ARTICLE_TEXT_AND_VOCABULARY_MEANING", StringComparison.Ordinal));
                Program.Check(log.Contains("\"level\":\"WARN\"", StringComparison.Ordinal));
            }
            finally { Directory.Delete(root, true); }
        });

        Program.Run("daily runtime log rotates within its per-file size limit", () =>
        {
            string root = NewDirectory();
            try
            {
                var logger = new AppLogger(root, maxFileBytes: 512, retentionDays: 30, maxTotalBytes: 4096);
                for (int i = 0; i < 30; i++) logger.Info("RepeatedDiagnosticEvent" + i.ToString("D2"));
                var files = Directory.GetFiles(root, "*.log");
                Program.Check(files.Length == 2);
                Program.Check(files.All(file => new FileInfo(file).Length <= 512));
            }
            finally { Directory.Delete(root, true); }
        });

        Program.Run("runtime logs prune expired files and enforce total storage limit", () =>
        {
            string root = NewDirectory();
            try
            {
                string expired = Path.Combine(root, "2000-01-01.log");
                File.WriteAllText(expired, "expired");
                File.SetLastWriteTime(expired, DateTime.Now.AddDays(-40));
                var logger = new AppLogger(root, maxFileBytes: 512, retentionDays: 14, maxTotalBytes: 1024);
                for (int i = 0; i < 40; i++) logger.Info("BoundedLogEvent" + i.ToString("D2"));
                Program.Check(!File.Exists(expired));
                var files = Directory.GetFiles(root, "*.log");
                Program.Check(files.Sum(file => new FileInfo(file).Length) <= 1024);
            }
            finally { Directory.Delete(root, true); }
        });

        Program.Run("runtime logging failures never escape into application code", () =>
        {
            string root = NewDirectory();
            try
            {
                string file = Path.Combine(root, "not-a-directory");
                File.WriteAllText(file, "block");
                var logger = new AppLogger(Path.Combine(file, "logs"));
                logger.Error("LogDirectoryUnavailable", new IOException("private path"));
                Program.Check(File.ReadAllText(file) == "block");
            }
            finally { Directory.Delete(root, true); }
        });
    }

    private static string NewDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReadArticles-logging-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
