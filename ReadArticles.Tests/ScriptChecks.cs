using System.IO;
using ReadArticles.Services;

internal static class ScriptChecks
{
    internal static void Run()
    {
        Program.Run("Script companion path is separate from Passage", () => WithArticle(path =>
        {
            Program.Check(ArticleRepository.ScriptPathFor(path) ==
                Path.ChangeExtension(path, ".script.json"));
        }));
        Program.Run("Script requires filetype and title but accepts empty title", () => WithArticle(path =>
        {
            var script = ArticleRepository.ScriptPathFor(path);
            File.WriteAllText(script, """{"filetype":"script","title":"","paragraphs":[[{"text":"One day.","timed_units":[["One",0.5,1.0]]}]]}""");
            var parsed = new ArticleRepository().LoadScript(path);
            Program.Check(parsed.Title == "" && parsed.Paragraphs.Count == 1 &&
                parsed.Paragraphs[0].Sentences[0].TimedUnits[0].Offset == 0);
            foreach (string invalid in new[]
            {
                """{"filetype":"script","paragraphs":[]}""",
                """{"filetype":"passage","title":"","paragraphs":[[{"text":"x","timed_units":[]}]]}""",
                """{"filetype":"script","title":null,"paragraphs":[[{"text":"x","timed_units":[]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",2,1]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",1,1]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",null,1]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",1,null]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",-1,1]]}]]}"""
            })
            {
                File.WriteAllText(script, invalid);
                try { new ArticleRepository().LoadScript(path); throw new Exception("Invalid Script accepted"); }
                catch (InvalidDataException) { }
            }
        }));
        Program.Run("Script retains repeated offsets and unaligned units", () => WithArticle(path =>
        {
            File.WriteAllText(ArticleRepository.ScriptPathFor(path),
                """{"filetype":"script","title":"Example","paragraphs":[[{"text":"the the stays","timed_units":[["the",0,1],["the",1,2],["missing",2,3],["stays",null,null]]}]]}""");
            var units = new ArticleRepository().LoadScript(path).Paragraphs[0].Sentences[0].TimedUnits;
            Program.Check(units[0].Offset == 0 && units[0].CanColor &&
                units[1].Offset == 4 && units[1].CanColor &&
                units[2].Offset == -1 && !units[2].CanColor &&
                units[3].Offset == 8 && !units[3].CanColor);
        }));
    }

    private static void WithArticle(Action<string> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ReadArticles-script-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "Example.json");
            check(path);
        }
        finally { Directory.Delete(dir, true); }
    }
}
