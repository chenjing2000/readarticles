using System.Reflection;
using System.Text.Json;
using ReadArticles.Models;

internal static class WebViewScriptBridgeChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 Script snapshot preserves timed-unit offsets and validity", () =>
        {
            var script = ScriptFixture();
            var serialize = typeof(ReadArticles.MainWindow).GetMethod(
                "WebPilotScriptSnapshot", BindingFlags.Static | BindingFlags.NonPublic);
            Program.Check(serialize is not null);
            object snapshot = serialize!.Invoke(null, [script])!;
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
            var root = json.RootElement;
            Program.Check(root.GetProperty("title").GetString() == "");
            var sentence = root.GetProperty("paragraphs").EnumerateArray().First()
                .EnumerateArray().First();
            Program.Check(sentence.GetProperty("text").GetString() == "non-identical grows");
            var units = sentence.GetProperty("timedUnits").EnumerateArray().ToArray();
            Program.Check(units.Length == 3);
            Program.Check(units[0].GetProperty("text").GetString() == "non-identical" &&
                units[0].GetProperty("offset").GetInt32() == 0 &&
                units[0].GetProperty("length").GetInt32() == 13 &&
                units[0].GetProperty("start").GetDouble() == 1 &&
                units[0].GetProperty("end").GetDouble() == 2 &&
                units[0].GetProperty("canColor").GetBoolean());
            Program.Check(units[1].GetProperty("offset").GetInt32() == -1 &&
                !units[1].GetProperty("canColor").GetBoolean());
            Program.Check(units[2].GetProperty("start").ValueKind == JsonValueKind.Null &&
                !units[2].GetProperty("canColor").GetBoolean());
        });

        Program.Run("reading workspace owns committed mode and resets only with article context", () =>
        {
            var workspace = new ReadArticles.Services.ReaderWorkspace();
            var article = new ReadArticles.Services.ArticleRepository().Load(TestData.ArticlePath);
            workspace.CommitArticle(article);
            var script = ScriptFixture();
            Program.Check(!workspace.IsScriptMode && workspace.Script is null);
            workspace.CommitReadingMode(true, script);
            Program.Check(workspace.IsScriptMode && ReferenceEquals(workspace.Script, script));
            Program.Check(ReferenceEquals(workspace.PrepareScript(), script));
            Program.Check(workspace.ValidateSelection(true, 0, 0, 0, 13, "non-identical"));
            Program.Check(!workspace.ValidateSelection(true, 0, 0, 2, 13, "non-identical"));
            Program.Check(!workspace.ValidateSelection(false, 0, 0, 0, 13, "non-identical"));
            workspace.CommitReadingMode(false, null);
            Program.Check(!workspace.IsScriptMode && ReferenceEquals(workspace.Script, script));
            workspace.CommitArticle(article);
            Program.Check(!workspace.IsScriptMode && workspace.Script is null);
            workspace.CommitReadingMode(true, script);
            workspace.ClearArticle();
            Program.Check(workspace.Article is null && workspace.Script is null && !workspace.IsScriptMode);
        });

        Program.Run("WebView2 Script selection validates sentence boundaries without SID", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var type = typeof(ReadArticles.MainWindow);
                Program.Check(window.ViewModel.OpenArticle(TestData.ArticlePath));
                window.ViewModel.CommitReadingMode(true, ScriptFixture());
                var verify = type.GetMethod("WebPilotValidateSelection",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Program.Check(verify is not null);
                Program.Check(Valid("""
                    {"mode":"script","paragraphIndex":0,"sentenceIndex":0,
                     "text":"non-identical","offset":0,"length":13}
                    """));
                Program.Check(!Valid("""
                    {"mode":"script","paragraphIndex":0,"sentenceIndex":1,
                     "text":"non-identical","offset":0,"length":13}
                    """));
                Program.Check(!Valid("""
                    {"mode":"script","paragraphIndex":0,"sentenceIndex":0,
                     "text":"different text","offset":0,"length":13}
                    """));
                Program.Check(!Valid("""
                    {"mode":"passage","sid":"s001","text":"non-identical",
                     "offset":0,"length":13}
                    """));

                bool Valid(string source)
                {
                    using var data = JsonDocument.Parse(source);
                    object?[] args = [data.RootElement, null];
                    bool accepted = (bool)verify!.Invoke(window, args)!;
                    return accepted && args[1] is string;
                }
            }
            finally { window.Close(); }
        });
    }

    private static ScriptArticle ScriptFixture() =>
        new("", [new ScriptParagraph([new ScriptSentence("non-identical grows", [
            new ScriptTimedUnit("non-identical", 1, 2, 0),
            new ScriptTimedUnit("unaligned", 2, 3, -1),
            new ScriptTimedUnit("grows", null, null, 14)
        ])])]);
}
