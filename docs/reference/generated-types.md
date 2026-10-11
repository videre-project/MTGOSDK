# Importing MTGO enum members

MTGOSDK.MSBuild refreshes marked regions **in the C# source file** before
compilation. The generated members are checked in and visible to editors,
reviewers, and other consumers of the repository. It reads constants from the
resolved reference assemblies without loading or starting MTGO.

## Begin/end regions

Keep the enum declaration and SDK additions outside the region:

```csharp
namespace MTGOSDK.API.Play.Games;

public enum GamePhase
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.GamePhase begin
  Invalid = 0,
  Untap = 1,
  // Remaining native members appear here in the actual file.
  //#pragma import enum WotC.MtGO.Client.Model.Play.GamePhase end

  PreGame1 = 13,
  PreGame2 = 14
}
```

The source name is a fully qualified metadata name. For nested types, use `+`,
such as `Native.Outer+Kind`. An optional `global::` prefix is accepted. Both
markers must name the same source type. Markers must occupy their own comment
lines. Multiple regions per file are supported; nested regions are rejected.

Only the text between the markers is replaced. The declaration, attributes,
comments, custom members, and both marker lines are preserved. Every imported
member receives its explicit native numeric value and a trailing comma, so SDK
members can follow it. Aliases, gaps, negative values, unsigned values, and
keyword member names are preserved.

The enum's **underlying type and attributes remain handwritten**. Specify the
matching underlying type when the native enum is not `int`, and declare
`[Flags]` yourself when required. Give SDK additions explicit values; an implicit
value after the region would change if the native member list changes. A native
member that conflicts with an SDK addition will produce a normal compiler error.

Examples of markers inside strings and block comments are ignored. Regions in
both branches of conditional compilation are refreshed, including inactive
branches, so their checked-in members remain current too. The task does not
parse or alter enum declarations; the C# compiler validates the resulting enum.

## Build and review

Build normally to refresh the regions:

```sh
dotnet build MTGOSDK/MTGOSDK.csproj -c Release
```

Then inspect the source diff. Repeated builds with unchanged metadata do not
rewrite the file or change its timestamp. File encoding, BOM, indentation, and
line endings are preserved. All regions and reference types are validated before
any source files are written. Each changed file is replaced atomically.

For CI, verify the committed regions without modifying them:

```sh
dotnet build MTGOSDK/MTGOSDK.csproj -c Release -p:CheckImportedEnums=true
```

This fails if a region is stale. After updating MTGO references, build normally
and commit the resulting changes. The task runs before `CoreCompile` even when
compilation would otherwise be skipped, but skips IDE design-time builds to avoid
rewriting source files in the background.

The task is included in `MTGOSDK.MSBuild.dll` and its `build`/`buildTransitive`
targets. The `src/SourceGenerators` project contains the separate
compiler generator for the remote access path registry; enum imports no longer
use an additive Roslyn source generator.

| Diagnostic | Cause |
| --- | --- |
| `MTGO001` | Invalid, nested, missing, or mismatched markers |
| `MTGO002` | Referenced type missing, ambiguous, or not an enum |
| `MTGO003` | Stale region in `CheckImportedEnums` mode |
| `MTGO004` | Source changed while the task was resolving imports |
