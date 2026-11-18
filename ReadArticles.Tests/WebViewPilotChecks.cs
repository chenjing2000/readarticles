using System.Reflection;

internal static class WebViewPilotChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 pilot assets embedded in application assembly", () =>
        {
            Assembly assembly = typeof(ReadArticles.MainWindow).Assembly;
            foreach (string name in new[]
            {
                "ReadArticles.web.reader.html",
                "ReadArticles.web.reader.css",
                "ReadArticles.web.reader.js"
            })
            {
                using Stream? stream = assembly.GetManifestResourceStream(name);
                Program.Check(stream is not null && stream.Length > 0);
            }
        });
    }
}
