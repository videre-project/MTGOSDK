/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Trade;
using MTGOSDK.API.Trade.Enums;


namespace MTGOSDK.Tests.MTGOSDK_API;

public class Trade : TradeValidationFixture
{
  [Test]
  public void Test_TradeManager()
  {
    foreach (TradePost post in TradeManager.AllPosts.Take(5))
      ValidatePost(post);

    var myPost = TradeManager.MyPost;
    if (myPost != null)
      ValidatePost(myPost);

    foreach (TradePartner partner in TradeManager.TradePartners.Take(5))
    {
      Assert.That(partner.Poster?.Id, Is.Not.Null.And.Not.EqualTo(-1));
      Assert.That(partner.LastTradeTime, Is.GreaterThan(DateTime.MinValue));
    }

    var currentTrade = TradeManager.CurrentTrade;
    if (currentTrade != null)
      ValidateTrade(currentTrade);
  }
}

public abstract class TradeValidationFixture : BaseFixture
{
  public void ValidatePost(TradePost post)
  {
    Assert.That(post.Poster?.Id, Is.Not.Null.And.Not.EqualTo(-1));
    Assert.That(post.Message, Is.Not.Null.Or.Empty);
    Assert.That(post.Format, Is.Not.EqualTo(TradePostFormat.Invalid));

    // Verify that no entries in the wanted or offered lists are empty
    Assert.That(post.Wanted.All(w => w.Quantity > 0 &&
                                     w.Card.Id > 0), Is.True);
    Assert.That(post.Offered.All(o => o.Quantity > 0 &&
                                      o.Card.Id > 0), Is.True);
  }

  public void ValidateTrade(TradeEscrow trade)
  {
    // TODO
  }
}
