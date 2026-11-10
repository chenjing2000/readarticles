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

    public static void RestoreLatestBackup(string sourcePath, string backupPath, bool keepExistingBackup = false)
    {
        string source = Path.GetFullPath(sourcePath);
        string backup = Path.GetFullPath(backupPath);
        if (!File.Exists(backup))
            throw new FileNotFoundException("没有可恢复的备份文件。", backup);

        string sourceDirectory = Path.GetDirectoryName(source)!;
        string backupDirectory = Path.GetDirectoryName(backup)!;
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(backupDirectory);

        string token = Guid.NewGuid().ToString("N");
        string restoreTemporary = Path.Combine(sourceDirectory, "." + Path.GetFileName(source) + "." + token + ".restore.tmp");
        string currentTemporary = Path.Combine(backupDirectory, "." + Path.GetFileName(backup) + "." + token + ".current.tmp");
        string previousTemporary = Path.Combine(backupDirectory, "." + Path.GetFileName(backup) + "." + token + ".previous.tmp");
        bool commitStarted = false;
        bool committed = false;
        bool safeToClean = false;
        try
        {
            // Stage and validate recovery input before any destructive rename.
            string backupFingerprint = JsonFiles.Fingerprint(backup);
            CopyAndVerify(backup, restoreTemporary, backupFingerprint);
            string? sourceFingerprint = File.Exists(source) ? JsonFiles.Fingerprint(source) : null;
            if (sourceFingerprint is not null && !keepExistingBackup)
            {
                CopyAndVerify(source, currentTemporary, sourceFingerprint);
                CopyAndVerify(backup, previousTemporary, backupFingerprint);
            }

            if ((File.Exists(source) ? JsonFiles.Fingerprint(source) : null) != sourceFingerprint ||
                JsonFiles.Fingerprint(backup) != backupFingerprint)
                throw new IOException("文件在恢复准备期间发生变化，未执行恢复。");

            commitStarted = true;
            if (sourceFingerprint is not null && !keepExistingBackup)
                File.Move(currentTemporary, backup, true);

            try
            {
                File.Move(restoreTemporary, source, true);
                committed = true;
                safeToClean = true;
            }
            catch
            {
                // A replace failure must not destroy the only previous valid backup.
                if (sourceFingerprint is not null && !keepExistingBackup && File.Exists(previousTemporary))
                {
                    try
                    {
                        File.Move(previousTemporary, backup, true);
                        safeToClean = true;
                    }
                    catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                    {
                        AppLogger.Default.Error("BackupRollbackFailed", rollbackError);
                        AppLogger.Default.Warning("RecoveryFilePreserved");
                        // Leave every recovery copy in place, including the prior backup.
                        throw new IOException("恢复与备份回滚均失败，已保留救援文件，需要人工恢复。", rollbackError);
                    }
                }
                else if (sourceFingerprint is null || keepExistingBackup)
                {
                    // Source and backup have not been rotated; both are still present.
                    safeToClean = true;
                }
                throw;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.Default.Error("BackupRestoreFailed", error);
            if (commitStarted && !committed && !safeToClean)
                AppLogger.Default.Warning("RecoveryFilePreserved");
            throw;
        }
        finally
        {
            // Never remove the sole copy on any uncertain post-commit failure.
            if (!commitStarted || committed || safeToClean)
            {
                TryDelete(restoreTemporary);
                TryDelete(currentTemporary);
                TryDelete(previousTemporary);
            }
        }
    }

    private static void CopyAndVerify(string inputPath, string stagedPath, string fingerprint)
    {
        using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
            output.Flush(true);
        }
        if (JsonFiles.Fingerprint(stagedPath) != fingerprint ||
            JsonFiles.Fingerprint(inputPath) != fingerprint)
            throw new IOException("恢复暂存文件校验失败，未执行恢复。");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.Default.Warning("RecoveryFilePreserved", error);
        }
    }
}
