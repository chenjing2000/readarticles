using ReadArticles.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ReadArticles.ViewModels;

namespace ReadArticles;

public partial class MainWindow
{
    private void IgnoreVocabularyRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right) e.Handled = true;
    }

    private void RefreshVocabularyHighlights()
    {
        var words = ViewModel.Words.Select(w => w.Word).ToArray();
        foreach (var reader in readers)
        {
            reader.SetVocabulary(words, ViewModel.HighlightsVisible);
        }
        HighlightButton.IsEnabled = ViewModel.Words.Count > 0;
        if (HighlightButton.IsEnabled)
            HighlightImage.Source = ViewModel.HighlightsVisible ? UiIcons.HighlightOff : UiIcons.HighlightOn;
        else
            HighlightImage.Source = ViewModel.HighlightsVisible ? UiIcons.DisabledHighlightOff : UiIcons.DisabledHighlightOn;
        HighlightButton.ToolTip = ViewModel.HighlightsVisible ? "隐藏正文词汇高亮" : "显示正文词汇高亮";
    }

    private void HighlightClicked(object sender, RoutedEventArgs e) => ViewModel.HighlightsVisible = !ViewModel.HighlightsVisible;

    private void VocabularySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshVocabularyActions();
        foreach (var reader in readers) reader.ClearVocabularyFocus();
        if (VocabularyList.SelectedItem is VocabularyEntryViewModel word)
        {
            foreach (var reader in readers)
            {
                if (reader.FocusVocabulary(word.Word)) break;
            }
        }
    }

    private void RefreshVocabularyActions()
    {
        var word = VocabularyList.SelectedItem as VocabularyEntryViewModel;
        VocabularyActions.Visibility = word is null ? Visibility.Collapsed : Visibility.Visible;
        MoveUpButton.IsEnabled = word?.MoveUp.CanExecute(null) == true;
        MoveDownButton.IsEnabled = word?.MoveDown.CanExecute(null) == true;
        DeleteWordButton.IsEnabled = word?.Delete.CanExecute(null) == true;
    }

    private void MoveSelectedWord(int direction)
    {
        if (VocabularyList.SelectedItem is not VocabularyEntryViewModel word) return;
        var command = direction < 0 ? word.MoveUp : word.MoveDown;
        if (!command.CanExecute(null)) return;
        command.Execute(null);
        VocabularyList.SelectedItem = ViewModel.Words.FirstOrDefault(w => ReferenceEquals(w.Entry, word.Entry));
        if (VocabularyList.SelectedItem is not null) VocabularyList.ScrollIntoView(VocabularyList.SelectedItem);
        RefreshVocabularyActions();
    }

    private void MoveUpClicked(object sender, RoutedEventArgs e) => MoveSelectedWord(-1);

    private void MoveDownClicked(object sender, RoutedEventArgs e) => MoveSelectedWord(1);

    private void DeleteWordClicked(object sender, RoutedEventArgs e)
    {
        if (VocabularyList.SelectedItem is VocabularyEntryViewModel word && word.Delete.CanExecute(null)) word.Delete.Execute(null);
    }

    private void ImportClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanWriteVocabulary)
        {
            ViewModel.Notify("VocabularyUnavailable", "请先打开有效文章和词汇表。", NotificationSeverity.Warning);
            return;
        }
        var dialog = new OpenFileDialog { Filter = "Vocabulary JSON|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "用导入词表替换当前词表？", "导入词汇表", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            ViewModel.ImportVocabulary(dialog.FileName);
        }
    }
}

