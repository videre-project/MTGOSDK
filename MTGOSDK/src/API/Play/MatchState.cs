/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play;

/// <summary>
/// The state of a match (e.g. "Joined", "GameStarted", "Sideboarding", etc.).
/// </summary>
/// <remarks>
/// Corresponds to flags specified in the <c>MatchStatuses</c> enum.
/// </remarks>
[Flags]
public enum MatchState : long
{
  Invalid                         = 0L,
  JoinRequested                   = 1L,
  Joined                          = 2L,
  RemovalRequested                = 4L,
  RecruitingFreezeRequested       = 8L,
  WatchRequested                  = 0x10L,
  Watching                        = 0x20L,
  AwaitingMinimumPlayers          = 0x40L,
  AwaitingMaximumPlayers          = 0x80L,
  AwaitingTimer                   = 0x100L,
  AwaitingPlayerStart             = 0x200L,
  AwaitingHostStart               = 0x400L,
  HostStartSent                   = 0x800L,
  GameStarted                     = 0x1000L,
  Resuming                        = 0x2000L,
  Sideboarding                    = 0x4000L,
  DeckBuilding                    = 0x8000L,
  DeckSubmitted                   = 0x10000L,
  DeckAccepted                    = 0x20000L,
  GameReplayRequested             = 0x40000L,
  GameReplaying                   = 0x80000L,
  MatchCompleted                  = 0x100000L,
  ConcedeRequested                = 0x200000L,
  Queue                           = 0x400000L,
  Tournament                      = 0x800000L,
  PremierEvent                    = 0x1000000L,
  Drafting                        = 0x2000000L,
  ChallengeMade                   = 0x4000000L,
  ChallengeAcceptanceSent         = 0x8000000L,
  ChallengeAccepted               = 0x10000000L,
  ChallengeRejectionSent          = 0x20000000L,
  ChallengeRetractionSent         = 0x40000000L,
  ChallengeMadeByCurrentUser      = 0x80000000L,
  ChallengeReceivedByCurrentUser  = 0x100000000L,
  EventUnderway                   = 0x200000000L,
  CurrentUserEliminated           = 0x400000000L,
  EventWaitingToStart             = 0x800000000L,
  EventCompleted                  = 0x1000000000L,
  Connecting                      = 0x2000000000L,
  Connected                       = 0x4000000000L,
  Terminal                        = 0x8000000000L,
  GameCompleted                   = 0x10000000000L,
  GameClosed                      = 0x20000000000L
}
