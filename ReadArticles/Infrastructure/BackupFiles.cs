using System.IO;
using System.Security.Cryptography;

namespace ReadArticles.Infrastructure;

public static class BackupFiles
{
    public static void CreateVerifiedBackup(string sourcePath, string backupPath)
    {
        string source = Path.GetFullPath(sourcePath);
        string backup = Path.GetFullPath(backupPath);
        if (!File.Exists(source))
            throw new FileNotFoundException("备份源文件不存在。", source);

        string directory = Path.GetDirectoryName(backup)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(backup) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] sourceBefore = SHA256.HashData(File.ReadAllBytes(source));
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }

            byte[] sourceAfter = SHA256.HashData(File.ReadAllBytes(source));
            byte[] copied = SHA256.HashData(File.ReadAllBytes(temporary));
            if (!CryptographicOperations.FixedTimeEquals(sourceBefore, sourceAfter) ||
                !CryptographicOperations.FixedTimeEquals(sourceBefore, copied))
                throw new IOException("备份期间源文件发生变化，备份校验失败。");

            File.Move(temporary, backup, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void RestoreLatestBackup(string sourcePath, string backupPath)
    {
        string source = Path.GetFullPath(sourcePath);
        string backup = Path.GetFullPath(backupPath);
        if (!File.Exists(backup))
            throw new FileNotFoundException("没有可恢复的备份文件。", backup);

        string sourceDirectory = Path.GetDirectoryName(source)!;
        string backupDirectory = Path.GetDirectoryName(backup)!;
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(backupDirectory);

        string restoreTemporary = Path.Combine(sourceDirectory, "." + Path.GetFileName(source) + "." + Guid.NewGuid().ToString("N") + ".restore.tmp");
        string backupTemporary = Path.Combine(backupDirectory, "." + Path.GetFileName(backup) + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp");
        bool backupReplaced = false;
        try
        {
            string expected = JsonFiles.Fingerprint(backup);
            using (var input = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(restoreTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            if (JsonFiles.Fingerprint(restoreTemporary) != expected)
                throw new IOException("备份文件复制校验失败，未执行恢复。");

            if (File.Exists(source))
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var output = new FileStream(backupTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                if (JsonFiles.Fingerprint(source) != JsonFiles.Fingerprint(backupTemporary))
                    throw new IOException("当前文件在恢复期间发生变化，未执行恢复。");
                File.Move(backupTemporary, backup, true);
                backupReplaced = true;
            }

            try
            {
                File.Move(restoreTemporary, source, true);
            }
            catch
            {
                if (backupReplaced && File.Exists(backupTemporary))
                    File.Move(backupTemporary, backup, true);
                throw;
            }
        }
        finally
        {
            if (File.Exists(restoreTemporary)) File.Delete(restoreTemporary);
            if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
        }
    }
}
