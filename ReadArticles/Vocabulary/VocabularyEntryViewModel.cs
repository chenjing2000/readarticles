using ReadArticles.Models;

namespace ReadArticles.ViewModels;

public sealed class VocabularyEntryViewModel
{
    public VocabularyEntry Entry { get; }
    public string Word => Entry.Word;
    public string PhoneticUk => string.IsNullOrWhiteSpace(Entry.PhoneticUk) ? "—" : Entry.PhoneticUk;
    public string PhoneticUs => string.IsNullOrWhiteSpace(Entry.PhoneticUs) ? "—" : Entry.PhoneticUs;
    public IReadOnlyList<Meaning> Meanings => Entry.Meanings;
    public bool CanPlayUk { get; }
    public bool CanPlayUs { get; }
    public VocabularyEntryViewModel(VocabularyEntry entry, bool uk, bool us)
    {
        Entry = entry;
        CanPlayUk = uk;
        CanPlayUs = us;
    }
}

