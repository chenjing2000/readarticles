# Getting started

Use Windows x64 with a compatible .NET 10 Windows Desktop Runtime x64 and the Microsoft Edge WebView2 Evergreen Runtime. End users do not need the SDK; the ordinary .NET Runtime does not include WPF.

Launch `dist/ReadArticles/ReadArticles.exe` from the portable folder (or its equivalent from a release archive) in a writable directory. Use the library folder selector to choose your own content. Library files and recordings remain external user data.

Settings are stored in `userdata/config/settings.json` beside the EXE. Old formats are intentionally not migrated. See [Configuration](Configuration.md) for recovery. Close the program before updating it, and keep the complete portable application folder together.

Developers need .NET 10 SDK (honor global.json where present). Use [Testing](Testing.md) for verification and [Publishing](Publishing.md) to build the portable EXE. Start with Core checks, then run UI/Full checks serially on the desktop.

## Detailed operation

- [Library layout](Library-Layout.md)
- [Vocabulary preparation](Vocabulary-Preparation.md)
- [Regression categories and manual acceptance](Testing.md)
- [Detailed user guide](User-Guide.md)