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
                AudioChecks.Run();
                ContentChecks.Run();
                ScriptChecks.Run();
                TreeNavigationChecks.Run();
                RealMediaChecks.Run();
                ReaderControlsChecks.Run();
                IconChecks.Run();
                WebViewPilotChecks.Run();
                WebViewPilotChecks.RunBrowser();
                WebViewScriptBridgeChecks.Run();
                WebViewVocabularyEditChecks.Run();
                WebViewSettingsChecks.Run();
                BackupChecks.Run();
                AppLoggerChecks.Run();
                NotificationChecks.Run();
                LazyTreeChecks.Run();
                break;
            case "--core-suite":
            case "--ui-suite":
                UnifiedAudioChecks.Run();
                ContentChecks.Run();
                ScriptChecks.Run();
                WebViewPilotChecks.Run();
                WebViewScriptBridgeChecks.Run();
                WebViewSettingsChecks.Run();
                WebViewPilotChecks.RunBrowser();
                TreeNavigationChecks.Run();
                WebViewVocabularyEditChecks.Run();
                NotificationChecks.Run();
                LazyTreeChecks.Run();
                if (category == "--ui-suite")
                {
                    IconChecks.Run();
                    ReaderControlsChecks.Run();
                    BackupChecks.Run();
                }
                break;
            case "--rendering-only": WebViewPilotChecks.RunBrowser(); WebViewScriptBridgeChecks.Run(); break;
            case "--audio-only": AudioChecks.Run(); RealMediaChecks.Run(); break;
            case "--content-only": UnifiedAudioChecks.Run(); ContentChecks.Run(); ScriptChecks.Run(); WebViewPilotChecks.Run(); WebViewScriptBridgeChecks.Run(); WebViewSettingsChecks.Run(); break;
            case "--script-only": ScriptChecks.Run(); WebViewScriptBridgeChecks.Run(); break;
            case "--webview-pilot-only": WebViewPilotChecks.Run(); WebViewPilotChecks.RunBrowser(); break;
            case "--unified-audio-only": UnifiedAudioChecks.Run(); break;
            case "--window-only": WebViewPilotChecks.Run(); WebViewPilotChecks.RunBrowser(); TreeNavigationChecks.Run(); break;
            case "--editing-only": WebViewVocabularyEditChecks.Run(); break;
            case "--icons-only": IconChecks.Run(); break;
            case "--controls-only": ReaderControlsChecks.Run(); WebViewSettingsChecks.Run(); BackupChecks.Run(); break;
            case "--backup-only": BackupChecks.Run(); break;
            case "--logging-only": AppLoggerChecks.Run(); break;
            case "--notifications-only": NotificationChecks.Run(); break;
            case "--lazy-tree-only": LazyTreeChecks.Run(); break;
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
