using System.IO;
using System.Text.Json;
using ReadArticles.Infrastructure;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public enum ReadingModePreparation { Ready, Unavailable, Failed }

public sealed partial class MainViewModel
{
    public bool IsScriptMode => workspace.IsScriptMode;
    public ScriptArticle? Script => workspace.Script;

    public ReadingModePreparation PrepareReadingMode(out bool nextScript, out ScriptArticle? script)
    {
        nextScript = !workspace.IsScriptMode;
        script = null;
        if (Article is not { } article) return ReadingModePreparation.Unavailable;
        if (!nextScript) return ReadingModePreparation.Ready;
        try
        {
            script = workspace.PrepareScript();
            if (script is not null) return ReadingModePreparation.Ready;
            Notify("ScriptNotFound",
                "Script 切换失败或不可用，已保留原阅读模式。未找到同名 Script JSON：" +
                Path.GetFileName(ArticleRepository.ScriptPathFor(article.FilePath)), NotificationSeverity.Error);
            return ReadingModePreparation.Unavailable;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or
            IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLogger.Default.Warning("WebPilotScriptLoadFailed", error);
            Notify("ScriptLoadFailed",
                "Script 切换失败或不可用，已保留原阅读模式。Script JSON 加载失败：" + error.Message,
                NotificationSeverity.Error);
            return ReadingModePreparation.Failed;
        }
    }

    public void CommitReadingMode(bool isScript, ScriptArticle? script) =>
        workspace.CommitReadingMode(isScript, script);

    public bool ValidateReadingSelection(bool isScript, int paragraphIndex, int sentenceIndex,
        int offset, int length, string? candidate) =>
        workspace.ValidateSelection(isScript, paragraphIndex, sentenceIndex, offset, length, candidate);
}
