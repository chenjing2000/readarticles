using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ReadArticles.Models;

public enum NodeLoadState { NotLoaded, Loading, Loaded, Failed }

public sealed class NavigationNode : INotifyPropertyChanged
{
    public string Name { get; }
    public string BookRoot { get; }
    public string? PassagePath { get; }
    public string? DirectoryPath { get; }
    public bool IsPassage => PassagePath is not null;
    public bool IsPlaceholder { get; }
    public ObservableCollection<NavigationNode> Children { get; }
    public IReadOnlyList<NavigationNode>? LastSuccessfulSnapshot { get; set; }
    public long Generation { get; private set; }
    private bool isExpanded;
    private NodeLoadState loadState;

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            if (!value)
            {
                Generation++;
                foreach (var child in Children.ToArray()) child.CollapseBranch();
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public NodeLoadState LoadState
    {
        get => loadState;
        set
        {
            if (loadState == value) return;
            loadState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadState)));
        }
    }

    public NavigationNode(string name, string bookRoot, string? passagePath,
        IReadOnlyList<NavigationNode> children, string? directoryPath = null, bool isPlaceholder = false)
    {
        Name = name;
        BookRoot = bookRoot;
        PassagePath = passagePath;
        DirectoryPath = directoryPath ?? (passagePath is null && !isPlaceholder ? bookRoot : null);
        IsPlaceholder = isPlaceholder;
        Children = new ObservableCollection<NavigationNode>(children);
        loadState = isPlaceholder || passagePath is not null ? NodeLoadState.Loaded : NodeLoadState.NotLoaded;
        if (!isPlaceholder && passagePath is null && Children.Count == 0)
            Children.Add(Placeholder(bookRoot));
    }

    public static NavigationNode Placeholder(string bookRoot) =>
        new("加载中…", bookRoot, null, Array.Empty<NavigationNode>(), null, true);

    public long NextGeneration() => ++Generation;

    public void ReplaceChildren(IEnumerable<NavigationNode> children, NodeLoadState state)
    {
        Children.Clear();
        foreach (var child in children) Children.Add(child);
        LoadState = state;
    }

    public void CollapseBranch()
    {
        Generation++;
        isExpanded = false;
        foreach (var child in Children.ToArray()) child.CollapseBranch();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
    }
}

public sealed record LibrarySnapshot(string Root, IReadOnlyList<NavigationNode> Books, IReadOnlyList<string> Warnings);
public sealed record DirectoryScanResult(IReadOnlyList<NavigationNode> Entries, IReadOnlyList<string> Warnings, bool IsComplete);
