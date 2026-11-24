# Contributing

Thanks for helping improve File Compare.

## Setup

Requires Windows 10 or 11 and the [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or newer.

```powershell
git clone https://github.com/vladano/win-file-compare.git
cd win-file-compare
dotnet build FileCompare.sln
dotnet run --project src/FileCompare.App
```

See [docs/BUILDING.md](docs/BUILDING.md) for the standalone exe and the installer.

## Before you open a pull request

- Run `dotnet test FileCompare.sln`. All tests must pass.
- Put comparison logic in `src/FileCompare.Core` (no UI) and add or update tests in `tests/FileCompare.Tests`.
- Check UI changes by hand. For the Explorer menu, drag and drop and the installer, use the [test checklist](docs/TESTING.md) or the opt-in UI tests (`scripts\run-ui-tests.ps1`).
- Keep pull requests focused on one change, and add a line to `CHANGELOG.md` under `Unreleased`.

## Reporting bugs and ideas

Use the [issue templates](https://github.com/vladano/win-file-compare/issues/new/choose). For a bug, include your Windows version, the File Compare version (Help > About > Copy details) and steps to reproduce. Check for an existing issue first.

For questions and open-ended ideas, use [Discussions](https://github.com/vladano/win-file-compare/discussions).

For security problems, do not open a public issue. See [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
