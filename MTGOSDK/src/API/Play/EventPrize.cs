/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Reflection.Serialization;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Play;

public class EventPrize(int count, int catalogId) : IJsonSerializable
{
  public int Id => catalogId;

  public int Count => count;

  public Card Item => CollectionManager.GetCard(catalogId);

  public EventPrize(CardQuantityPair pair) : this(pair.Quantity, pair.Id)
  {
  }

  private static string GetRankOrdinal(int rank)
  {
    var s = rank.ToString();
    rank %= 100;
    return s + (rank is >= 11 and <= 13
      ? "th"
      : (rank % 10) switch
      {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th"
      });
  }

  public static Dictionary<string, IList<EventPrize>> FromPrizeStructure(
    dynamic prizeStructure,
    bool top8 = false)
  {
    Dictionary<string, IList<EventPrize>> prizes = new();
    int maxBound = ((IEnumerable<dynamic>)prizeStructure).Max(t => t.UpperBound);
    foreach (var tier in prizeStructure)
    {
      int lowerBound = tier.LowerBound;
      string bracket;
      if (top8)
      {
        int upperBound = tier.UpperBound;
        bracket = lowerBound == upperBound
          ? GetRankOrdinal(lowerBound)
          : string.Format("{0}-{1}",
              GetRankOrdinal(lowerBound),
              GetRankOrdinal(upperBound));
      }
      else
      {
        int wins = lowerBound / 3;
        int losses = (maxBound - lowerBound) / 3;
        bracket = string.Format("{0}-{1}", wins, losses);
      }

      List<EventPrize> digitalObjects = [];
      foreach(var kvp in Map<IDictionary, int, int>(tier.DigitalObjects))
      {
        int catalogId = kvp.Key;
        int count = kvp.Value;
        digitalObjects.Add(new EventPrize(count, catalogId));
      }

      prizes[bracket] = digitalObjects;
    }

    return prizes;
  }
}
