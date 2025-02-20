/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model.Play;


namespace MTGOSDK.API.Play;

public sealed class EntryFeeSuite(dynamic entryFees) : DLRWrapper<IEntryFeeSuite>
{
  /// <summary>
  /// Stores an internal reference to the IEntryFeeSuite object.
  /// </summary>
  internal override dynamic obj => Bind<IEntryFeeSuite>(entryFees);

  public class EntryFee(CardQuantityPair pair) : EventPrize(pair)
  {
  }

  //
  // IEntryFeeSuite wrapper properties
  //

  public int Id => @base.Id;

  public string Name => @base.Name;

  public IList<EntryFee> EntryFees
  {
    get
    {
      List<EntryFee> entryFees = new();
      foreach (var entryFee in @base.EntryFees)
      {
        foreach (var item in Map<CardQuantityPair>(entryFee.Items))
        {
          entryFees.Add(new(item));
        }
      }

      return entryFees;
    }
  }

  public bool IsFreeToPlay => @base.IsFreeToPlay;
}
