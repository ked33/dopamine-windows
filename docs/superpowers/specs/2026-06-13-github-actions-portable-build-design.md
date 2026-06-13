# GitHub Actions Portable Build Design

## Context

The local machine does not have the build environment required for this Windows WPF application, so the repository needs a GitHub Actions workflow that builds in the cloud after changes are pushed.

The project is an old-style Visual Studio/.NET Framework solution:

- `Dopamine.sln` uses Visual Studio 15 solution format.
- `Dopamine/Dopamine.csproj` targets .NET Framework 4.8 and builds the main WPF executable.
- `Dopamine.Packager/PackagerConfiguration.xml` defines the Portable package directories and files.
- The README documents two Windows SDK/reference assembly requirements:
  - `C:\Program Files (x86)\Windows Kits\10\UnionMetadata\Windows.winmd`
  - `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETCore\v4.5\System.Runtime.WindowsRuntime.dll`

## Selected Approach

Create `.github/workflows/build-portable.yml`.

The workflow will:

1. Run on `push` to `master` and on manual `workflow_dispatch`.
2. Use a Windows GitHub-hosted runner.
3. Restore NuGet packages for `Dopamine.sln`.
4. Ensure `Windows.winmd` exists at the hard-coded path expected by the project by copying it from an installed Windows SDK version when needed.
5. Build `Dopamine/Dopamine.csproj` with MSBuild using `Release|AnyCPU`.
6. Parse `Dopamine.Packager/PackagerConfiguration.xml`.
7. Copy only the configured Portable directories and files from `Dopamine/bin/Release` into a staging directory.
8. Fail explicitly if a configured Portable item is missing.
9. Compress the staging directory into a zip file.
10. Upload the zip as a GitHub Actions artifact.

## Alternatives Considered

### Run `Dopamine.Packager.exe`

This is closer to the original packaging intent, but it is broader than the requested Portable artifact. The packager configuration includes publishing and installable/WiX settings, so invoking the executable in CI risks triggering behavior unrelated to Portable packaging.

### Upload Entire `Dopamine/bin/Release`

This is simpler, but the artifact boundary is too loose. It may include files not listed in the Portable package definition and would not catch missing Portable-only content.

## Error Handling

The workflow should stop on the first failed command. Packaging should validate each configured directory and file before copying. If a Portable item is missing from `Dopamine/bin/Release`, the workflow should fail with a readable message naming the missing item.

## Verification

Local verification is limited because this machine has no build environment. After implementation, local checks should cover only the workflow file and script structure. The actual build and package validation will be performed by GitHub Actions after pushing to the remote repository.

## Expected Output

The workflow artifact should contain a zip named with the source commit, for example:

`Dopamine-Portable-${{ github.sha }}.zip`

The zip should contain the Portable package contents at the archive root, including `Dopamine.exe`, runtime DLLs, language files, icons, equalizer presets, FFmpeg binaries, and architecture-specific native folders as defined by `PackagerConfiguration.xml`.
