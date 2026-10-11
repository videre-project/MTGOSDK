/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.ObjectModel;

using MTGOSDK.API.Interface.Controls;


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// Values captured together from a game's duel scene, associated with its native prompt.
/// Collections contain detached values.
/// </summary>
public sealed record InteractionStateSnapshot(
  int? PromptNonce,
  InteractionMode Mode,
  string? PromptBoxText,
  IReadOnlyList<PromptButton> Buttons,
  IReadOnlyList<PlayerSelectable> Players,
  // Keys are Diver logical addresses, independent of MTGO Id and SourceId.
  IReadOnlyDictionary<ulong, CardInteractionSnapshot> Cards,
  long Version
)
{
  // The stream and revision distinguish gameplay messages whose prompt nonces
  // are identical. The sequence identifies local captures within that revision.
  internal Guid CaptureStream { get; init; }
  internal long StateRevision { get; init; }
  internal long CaptureSequence { get; init; }

  internal static InteractionStateSnapshot Empty { get; } = new(
    null, InteractionMode.Undefined, null, Array.Empty<PromptButton>(),
    Array.Empty<PlayerSelectable>(),
    new ReadOnlyDictionary<ulong, CardInteractionSnapshot>(new Dictionary<ulong, CardInteractionSnapshot>()), 0);
}
