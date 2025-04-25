/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;

/// <summary>
/// Represents the event structure of an event queue.
/// </summary>
public partial class EventStructure(Queue queue, dynamic tournamentStructure)
    : DLRWrapper<ITournamentStructure>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(ITournamentStructure);

  /// <summary>
  /// Stores an internal reference to the ITournamentStructure object.
  /// </summary>
  internal override dynamic obj =>
    Bind<ITournamentStructure>(tournamentStructure);

  private TournamentStructureValue m_tournamentStructure =>
    Cast<TournamentStructureValue>(Unbind(@base).Value);

  private LimitedTournamentStyle m_limitedTournamentStyle =>
    Cast<LimitedTournamentStyle>(Unbind(queue).LimitedTournamentStyle);

  private DeckCreationStyle m_deckCreationStyle =>
    Cast<DeckCreationStyle>(Unbind(queue).DeckCreationStyle);

  private TournamentEliminationStyle m_eliminationStyle =>
    Cast<TournamentEliminationStyle>(Unbind(queue).TournamentEliminationStyle);

  //
  // ITournamentStructure wrapper properties
  //

  public string Name => @base.Name;

  public bool IsConstructed =>
    this.m_deckCreationStyle == DeckCreationStyle.Constructed;

  public bool IsLimited =>
    this.m_deckCreationStyle == DeckCreationStyle.Limited;

  public bool IsDraft =>
    this.m_limitedTournamentStyle == LimitedTournamentStyle.Draft;

  public bool IsSealed =>
    this.m_limitedTournamentStyle == LimitedTournamentStyle.Sealed;

  public bool IsSingleElimination =>
    this.m_eliminationStyle == TournamentEliminationStyle.SingleElimination;

  public bool IsSwiss =>
    this.m_eliminationStyle == TournamentEliminationStyle.Swiss;

  public bool HasPlayoffs =>
    this.m_tournamentStructure switch
    {
      TournamentStructureValue.ConstructedPremierScheduled => true,
      TournamentStructureValue.SealedPremierScheduled => true,
      TournamentStructureValue.DraftPremierScheduled => true,
      _ => false
    };
}
