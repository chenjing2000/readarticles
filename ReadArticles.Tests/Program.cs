using System.Windows;

internal static class Program
{
    private static int failures;
    private static int passed;

    [STAThread]
    static int Main(string[] args)
    {
        var app = new ReadArticles.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string category = args.Length == 0 ? "all" : args[0];
        switch (category)
        {
            case "all":
                UnifiedAudioChecks.Run();
                MatchingChecks.Run();
                RenderingChecks.Run();
                AudioChecks.Run();
                WindowChecks.Run();
                ContentChecks.Run();
                ReaderWindowChecks.Run();
                TreeNavigationChecks.Run();
                RealMediaChecks.Run();
                ReaderControlsChecks.Run();
                IconChecks.Run();
                VocabularyEditingChecks.Run();
                break;
            case "--rendering-only": MatchingChecks.Run(); RenderingChecks.Run(); break;
            case "--audio-only": AudioChecks.Run(); RealMediaChecks.Run(); break;
            case "--content-only": UnifiedAudioChecks.Run(); ContentChecks.Run(); break;
            case "--unified-audio-only": UnifiedAudioChecks.Run(); break;
            case "--window-only": WindowChecks.Run(); ReaderWindowChecks.Run(); break;
            case "--editing-only": VocabularyEditingChecks.Run(); break;
            case "--icons-only": IconChecks.Run(); break;
            case "--controls-only": ReaderControlsChecks.Run(); break;
            case "--tree-only": TreeNavigationChecks.Run(); break;
            case "--source-only": SourceLibraryChecks.Run(); break;
            default:
                Console.Error.WriteLine("Unknown test category: " + category);
                return 2;
        }
        Console.WriteLine($"Passed: {passed}; Failed: {failures}");
        return failures == 0 ? 0 : 1;
    }
    internal static void Check(bool condition)
    {
        if (!condition) throw new Exception("Assertion failed");
    }
    internal static void Run(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
