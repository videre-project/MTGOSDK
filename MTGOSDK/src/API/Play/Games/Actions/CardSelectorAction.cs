/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play.Games;

public sealed class CardSelectorAction(dynamic cardSelectorAction) : GameAction
{
  /// <summary>
  /// Stores an internal reference to the ICardSelectorAction object.
  /// </summary>
  internal override dynamic obj => Bind<ICardSelectorAction>(cardSelectorAction);

  public struct CardSelectorChoice(dynamic cardSelectorChoice)
  {
    public int CTN = cardSelectorChoice.CTN;
    public bool CanChoose = cardSelectorChoice.CanChoose;
  }

  //
  // ICardSelectorAction wrapper properties
  //

  public List<CardSelectorChoice> Choices =>
    Map<CardSelectorChoice>((object)Unbind(this).Choices,
      item => new CardSelectorChoice(item)).ToList();

  // TODO: Map CTN to GameCard
  //       (i.e. ICardDataManager.GetCardDefinitionForTextureNumber(CTN))
  public int SelectedCard => Unbind(this).SelectedCard;
}
