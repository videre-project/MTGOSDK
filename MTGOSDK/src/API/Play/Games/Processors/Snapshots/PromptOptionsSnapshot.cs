/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play.Games.Processors.Snapshots;

/// <summary>
/// Prompt options and the client UI signature for a single game prompt.
/// </summary>
public sealed record PromptOptionsSnapshot(
  int PromptNonce,
  long ClientRevision,
  GamePrompt Prompt
)
{
  /// <summary>
  /// The captured prompt's grouped actions and client-local controls.
  /// </summary>
  public IReadOnlyDictionary<ActionType, IReadOnlyList<IPromptOption>> Options =>
    Prompt.Options;
}
