# Windows distribution

## Build a standalone release

On Windows with a compatible .NET 10 SDK, run from the source directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Publish-Windows.ps1
```

The script creates `dist/GameLink-v0.2.9.2-win-x64.zip` and prints its SHA-256 hash. The publish profile bundles the .NET Windows Desktop runtime and native SQLite libraries into the single-file executable (native libraries are extracted by .NET at runtime). Trimming is disabled for WinForms and serialization compatibility. Keep any additional published files with the executable. The script refuses to overwrite an existing staging directory to protect data.

## Run the application

Extract the entire ZIP to a writable folder, then launch GameLink.exe. No separate .NET runtime or Visual Studio should be required on a supported x64 Windows system. The first-run wizard offers standard or portable storage. Standard mode uses %AppData%/GameLink; portable mode uses Data beside the executable. Do not run from inside the ZIP. Keep portable data when upgrading; close GameLink before replacing executable files. The executable is unsigned, so Windows may show a reputation warning. Verify the release source and hash before running it. No installer is included in this iteration.

## Release validation

Test on a clean Windows x64 machine without a separately installed .NET Desktop runtime. Verify launch, SQLite history, DPAPI password storage, Steam discovery, artwork, Streamer.bot delivery, first-run setup, tray and startup registration. Test upgrading an existing standard and portable installation. Do not distribute publicly until these tests pass. The source package itself does not contain a compiled executable.
