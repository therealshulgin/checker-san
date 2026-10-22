# Checker San

Windows 11 app that checks whether two folders contain the same files, and finds duplicates inside a folder. Works with local folders, mapped drives and `\\PC\Share` paths. Interface in English or French.

- **Compare** two folders in both directions: identical, missing, changed and moved files, confirmed by full SHA-256. Export to CSV or JSON.
- **Complete** either folder with its missing files. Nothing is overwritten or deleted.
- **Find duplicates**, then move the copies you choose to a quarantine folder or delete them.
- Stop and resume scans and copies at any time.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
.\build.ps1 -Test
```

Run `dist\CheckerSan\CheckerSan.exe`. It is standalone: .NET is not needed to run it.

## Limits

Links and junctions are reported, not followed. Permissions and alternate data streams are not compared.
