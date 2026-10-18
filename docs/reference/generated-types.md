# Importing MTGO enum members

MTGOSDK.MSBuild refreshes marked regions **in the C# source file** before
compilation. The generated members are checked in and visible to editors,
reviewers, and other consumers of the repository. It reads constants from the
resolved reference assemblies without loading or starting MTGO.

## Begin/end regions

Keep the enum declaration and SDK additions outside the region:

```csharp
namespace MTGOSDK.API.Play.Games;

public enum ActionType
{
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionType begin
  Invalid = 0,
  ChooseOption = 1,
  CardAction = 2,
  // Remaining native members appear here in the actual file.
  //#pragma import enum WotC.MtGO.Client.Model.Play.ActionType end

  /// <summary>
  /// SDK-only client prompt button without a native game action.
  /// </summary>
  PromptControl = int.MaxValue
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

## Current imports

| SDK enum | MTGO enum | Reference assembly |
| --- | --- | --- |
| `ChannelType` | `WotC.MtGO.Client.Model.Chat.ChatChannelType` | `WotC.MtGO.Client.Model.Reference` |
| `CardGroupingType` | `WotC.MtGO.Client.Model.Data.CardGroupingType` | `WotC.MtGO.Client.Model.Data` |
| `DeckRegion` | `WotC.MTGO.Common.DeckRegionEnum` | `MTGOEnumStruct` |
| `SetType` | `WotC.MTGO.Common.CardSetTypeEnum` | `MTGOEnumStruct` |
| `ActionModifiers` | `WotC.MtGO.Client.Model.Play.ActionModifiers` | `WotC.MtGO.Client.Model.Reference` |
| `ActionTargetRequirements` | `WotC.MtGO.Client.Model.Play.Enums.ActionTargetRequirements` | `WotC.MtGO.Client.Model.Reference` |
| `ActionType` | `WotC.MtGO.Client.Model.Play.ActionType` | `WotC.MtGO.Client.Model.Reference` |
| `CardAbility` | `WotC.MtGO.Client.Model.MagicCardAbility` | `WotC.MtGO.Client.Model.Reference` |
| `CardAssociation` | `WotC.MtGO.Client.Model.Play.CardAssociation` | `WotC.MtGO.Client.Model.Reference` |
| `CardCounter` | `WotC.MtGO.Client.Model.Play.Counter` | `WotC.MtGO.Client.Model.Reference` |
| `CardZone` | `WotC.MtGO.Client.Model.Play.CardZone` | `WotC.MtGO.Client.Model.Reference` |
| `FunctionKey` | `WotC.MtGO.Client.Model.Play.Actions.FunctionKeyMessageAction+FunctionKey` | `WotC.MtGO.Client.Model.Play` |
| `GamePhase` | `WotC.MtGO.Client.Model.Play.GamePhase` | `WotC.MtGO.Client.Model.Reference` |
| `GameStatus` | `WotC.MtGO.Client.Model.Play.GameState` | `WotC.MtGO.Client.Model.Reference` |
| `InteractionMode` | `Shiny.Play.Duel.Utility.InteractMode` | `DuelScene` |
| `MagicProperty` | `WotC.MtGO.Client.Model.MagicProperty` | `Mtgo.Shared.Parser.Attributes` |
| `MagicColors` | `WotC.MtGO.Client.Model.MagicColors` | `Mtgo.Shared.Parser.Attributes` |
| `StateElementType` | `WotC.MtGO.Client.Model.Play.StateElementType` | `WotC.MtGO.Client.Model.Play` |
| `MatchState` | `WotC.MtGO.Client.Model.Play.Enums.MatchStatuses` | `WotC.MtGO.Client.Model.Reference` |
| `PlayFormatType` | `WotC.MtGO.Client.Model.Collection.PlayFormatType` | `WotC.MtGO.Client.Model.Reference` |
| `QueueState` | `WotC.MtGO.Client.Model.Play.Enums.QueueState` | `WotC.MtGO.Client.Model.Reference` |
| `TournamentEliminationStyle` | `WotC.MtGO.Client.Model.Play.TournamentEliminationStyle` | `WotC.MtGO.Client.Model.Reference` |
| `TournamentState` | `WotC.MtGO.Client.Model.Play.Tournaments.TournamentState` | `WotC.MtGO.Client.Model.Reference` |
| `Setting` | `WotC.MtGO.Client.Model.Settings.SettingName` | `WotC.MtGO.Client.Model.Reference` |
| `TradeError` | `WotC.MtGO.Client.Model.Trade.Enums.ClientTradeErrorEnum` | `WotC.MtGO.Client.Model.Reference` |
| `TradeFinalState` | `WotC.MtGO.Client.Model.Trade.Enums.TradeFinalState` | `WotC.MtGO.Client.Model.Reference` |
| `TradePostFormat` | `WotC.MtGO.Client.Model.Trade.TradePostFormat` | `WotC.MtGO.Client.Model.Data` |
| `TradeState` | `WotC.MtGO.Client.Model.Trade.Enums.TradeState` | `WotC.MtGO.Client.Model.Reference` |

`ActionType.PromptControl` is an SDK addition outside its import region.
`GamePhase.PreGame1 = 13` and `PreGame2 = 14` likewise remain explicit SDK
additions outside the imported native members.
`MagicProperty.GS_SPLITCARD_ID1` is an SDK alias for the unnamed second
split-card slot accessed by MTGO, defined as `GS_SPLITCARD_ID0 + 1` outside the
import region. Spell and planeswalker subtypes use the current native
`SPELL_TYPEID0` and `PLANESWALKER_TYPEID0` ranges. Legacy spell subtype flags and
`SUBTYPEID0` are not vendored into the enum.
Artifact subtypes are read from the native `ARTIFACT_TYPEID0` range and interpreted
using `MagicArtifactType`; unverified artifact boolean flags are not retained.
Snapshot parsing retains each native numeric property ID and its native name,
including properties absent from the SDK enum. It does not remap IDs by name,
so capture tolerates an incomplete enum. A difference between native and SDK
declarations alone does not establish that snapshot processing is broken.

The imports also replace previously stale copies: `ActionModifiers`,
`ActionTargetRequirements`, and `FunctionKey` gain current native members;
`StateElementType` no longer includes `CasualWish` and uses current native
numbering. `Setting` follows `SettingName`, including the separate
`KeyBindingLargeCardView` and `KeyBindingManualZoom` entries in place of the
obsolete SDK `KeyBindingZoom` name. `TradeError` uses native
`NoDepositListSpecified`, correcting the SDK's former spelling.

`DeckRegion` uses the native `int` underlying type. `MatchState` remains `long`
and `FunctionKey` remains `byte`, matching their native declarations. The SDK
explicitly references `WotC.MtGO.Client.Model.Data` for `CardGroupingType` and
`TradePostFormat`.

`DeckImportMode`, `EventManager.EventCollection`, `PromptButtonGroup`,
`PlayDrawResult`, `GameResult`, and `MatchResult` remain handwritten SDK
contracts rather than direct native enum copies.

`GameResult` adapts the final-result members of
`WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game+FlsGameResult`:
SDK `Win = 2` corresponds to native `Won = 2`, and `Loss = 3` to `Lost = 3`.
The SDK omits native `StillPlaying = 1` and retains its `int` underlying type
(the native enum uses `byte`). `WotC.MTGO.Common.GameResultCodeEnum` uses the
same numeric codes with `STILL_PLAYING`, `WIN`, and `LOSS` names. The separate
`WotC.MtGO.Client.Model.Play.GameResultType` uses `Win = 1` and `Loss = 2`,
so it is not the encoding read by `GameResultsChanged`.

New counters appear when **the SDK is rebuilt against updated MTGO references**.
An already compiled SDK package does not gain enum members when MTGO updates.
The build copies the actual constant values from its references, rather than
inferring them from a hand-maintained ordering.

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
