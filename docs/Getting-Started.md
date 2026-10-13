# Getting started

Use Windows x64 with a compatible .NET 10 Windows Desktop Runtime x64. End users do not need the SDK; the ordinary .NET Runtime does not include WPF.

Launch dist/ReadArticles.exe or the EXE downloaded from GitHub Releases in a writable directory. Use the application's folder/dictionary selector to choose your own content. Libraries, dictionaries, and recordings are external user data.

Settings are stored beside the EXE as ReadArticles.settings.json. See [Configuration](Configuration.md) for naming and recovery. Close the program before replacing the EXE and preserve existing user files.

Developers need .NET 10 SDK (honor global.json where present). Use [Testing](Testing.md) for verification and [Publishing](Publishing.md) to build the portable EXE. Start with Core checks, then run UI/Full checks serially on the desktop.

## Detailed operation

- [Library layout](Library-Layout.md)
- [Vocabulary preparation](Vocabulary-Preparation.md)
- [Detailed regression categories](Testing-Reference.md)
- [Detailed user guide](User-Guide.md)