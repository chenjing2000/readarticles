using System.Windows;
using Microsoft.Win32;
using ReadArticles.Presentation;

namespace ReadArticles;

public partial class MainWindow
{
    // Initialized after XAML controls exist. SizeChanged may run during construction.
    private WebReaderHost? webReaderHost;
    private WebReaderBridge? webReaderBridge;

    private void InitializeWebReader()
    {
        webReaderHost = new WebReaderHost(ReaderWebView);
        webReaderBridge = new WebReaderBridge(webReaderHost, ViewModel, settings, paneLayout,
            AvailablePaneWidth, ApplyPaneWidths, ChooseVocabularyImport,
            rendered => ReaderOverlay.Visibility = rendered ? Visibility.Visible : Visibility.Collapsed);
        webReaderHost.Failed += ShowWebReaderFailure;
        Loaded += WebReaderLoaded;
    }

    private async void WebReaderLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebReaderLoaded;
        if (webReaderHost is not null) await webReaderHost.InitializeAsync();
    }

    private string? ChooseVocabularyImport()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Vocabulary JSON|*.json",
            CheckFileExists = true
        };
        return dialog.ShowDialog(this) == true &&
            MessageBox.Show(this, "用导入词表替换当前词表？",
                "导入词汇表", MessageBoxButton.YesNo) == MessageBoxResult.Yes
            ? dialog.FileName : null;
    }

    private void ShowWebReaderFailure(string message)
    {
        WebFailureMessage.Text = message;
        WebFailurePanel.Visibility = Visibility.Visible;
        ReaderOverlay.Visibility = Visibility.Collapsed;
        WebFailureExitButton.Focus();
    }

    private void WebFailureExitClicked(object sender, RoutedEventArgs e) => Close();

    private void CloseWebReader()
    {
        Loaded -= WebReaderLoaded;
        webReaderBridge?.Dispose();
        if (webReaderHost is not null)
        {
            webReaderHost.Failed -= ShowWebReaderFailure;
            webReaderHost.Dispose();
        }
    }
}
