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
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.MatchStatuses begin
  Invalid = 0L,
  JoinRequested = 1L,
  Joined = 2L,
  RemovalRequested = 4L,
  RecruitingFreezeRequested = 8L,
  WatchRequested = 16L,
  Watching = 32L,
  AwaitingMinimumPlayers = 64L,
  AwaitingMaximumPlayers = 128L,
  AwaitingTimer = 256L,
  AwaitingPlayerStart = 512L,
  AwaitingHostStart = 1024L,
  HostStartSent = 2048L,
  GameStarted = 4096L,
  Resuming = 8192L,
  Sideboarding = 16384L,
  DeckBuilding = 32768L,
  DeckSubmitted = 65536L,
  DeckAccepted = 131072L,
  GameReplayRequested = 262144L,
  GameReplaying = 524288L,
  MatchCompleted = 1048576L,
  ConcedeRequested = 2097152L,
  Queue = 4194304L,
  Tournament = 8388608L,
  PremierEvent = 16777216L,
  Drafting = 33554432L,
  ChallengeMade = 67108864L,
  ChallengeAcceptanceSent = 134217728L,
  ChallengeAccepted = 268435456L,
  ChallengeRejectionSent = 536870912L,
  ChallengeRetractionSent = 1073741824L,
  ChallengeMadeByCurrentUser = 2147483648L,
  ChallengeReceivedByCurrentUser = 4294967296L,
  EventUnderway = 8589934592L,
  CurrentUserEliminated = 17179869184L,
  EventWaitingToStart = 34359738368L,
  EventCompleted = 68719476736L,
  Connecting = 137438953472L,
  Connected = 274877906944L,
  Terminal = 549755813888L,
  GameCompleted = 1099511627776L,
  GameClosed = 2199023255552L,
  //#pragma import enum WotC.MtGO.Client.Model.Play.Enums.MatchStatuses end
}
