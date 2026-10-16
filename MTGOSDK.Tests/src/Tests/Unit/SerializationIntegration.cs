/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Serialization;

using AttributeAnnotation = WotC.MtGO.Client.Model.Collection.AttributeAnnotation;
using ITradePost = WotC.MtGO.Client.Model.Trade.ITradePost;


namespace MTGOSDK.UnitTests;

[NonParallelizable]
public class SerializationIntegration
{
  public interface IMessage
  {
    string Message { get; }
  }

  public interface IPost
  {
    string Message { get; }
    IEnumerable<CardQuantityPair> Wanted { get; }
  }

  public interface IObservedMessage
  {
    string Message { get; }
    string MessageAfterReadingWanted { get; }
  }

  public sealed class ObservedTradePost(object source) : TradePost(source)
  {
    public string MessageAfterReadingWanted
    {
      get
      {
        _ = Wanted;
        return Message;
      }
    }
  }

  public sealed class TradePostSource : ITradePost
  {
    private string _message = "first";
    private int _wantedReads;
    public int WantedReads => Volatile.Read(ref _wantedReads);

    public string RawMessage
    {
      get => Volatile.Read(ref _message);
      set => Volatile.Write(ref _message, value);
    }

    public object? Wanted
    {
      get
      {
        Interlocked.Increment(ref _wantedReads);
        return null;
      }
    }

    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
  }

  [TestCase(AttributeAnnotation.DeckbuildingColorGreen, 16u)]
  [TestCase(AttributeAnnotation.DeckbuildingColorWhite | AttributeAnnotation.DeckbuildingColorGreen, 17u)]
  public void CollectionSnapshotsPreserveAnnotationBits(AttributeAnnotation annotation, uint expected)
  {
    using var peer = new SerializationPeer();
    var items = peer.Add(new[]
    {
      new { CatalogId = 65378, Annotation = annotation, Quantity = 2 }
    });
    var row = DLRWrapper.SerializeDroAs<ICardGroupingItemSnapshot>(items,
      new[] { "CatalogId", "Annotation.EnumValue", "Quantity" }).Single();
    Assert.That(row.CatalogId, Is.EqualTo(65378));
    Assert.That(row.Quantity, Is.EqualTo(2));
    Assert.That(row.Annotation, Is.EqualTo(expected));
  }

  [TestCase(false)]
  [TestCase(true)]
  public async Task RepeatedSerializationCapturesTheCurrentSourceValue(bool asynchronous)
  {
    using var peer = new SerializationPeer();
    var source = new TradePostSource();
    var post = new TradePost(peer.Add(source));
    var first = asynchronous ? await post.SerializeAsAsync<IMessage>() : post.SerializeAs<IMessage>();
    source.RawMessage = "second";
    var second = asynchronous ? await post.SerializeAsAsync<IMessage>() : post.SerializeAs<IMessage>();
    Assert.That(first.Message, Is.EqualTo("first"));
    Assert.That(second.Message, Is.EqualTo("second"));
    Assert.That(post.Message, Is.EqualTo("second"));
    Assert.That(peer.BatchRequests, Is.EqualTo(2));
  }

  [Test]
  public void RepeatedJsonSerializationCapturesTheCurrentSourceValue()
  {
    using var peer = new SerializationPeer();
    var source = new TradePostSource();
    var post = new TradePost(peer.Add(source));
    post.SetSerializationProperties(include: new[] { "Message" }, strict: true);
    using var first = JsonDocument.Parse(post.ToJSON());
    source.RawMessage = "second";
    using var second = JsonDocument.Parse(post.ToJSON());
    Assert.That(first.RootElement.GetProperty("message").GetString(), Is.EqualTo("first"));
    Assert.That(second.RootElement.GetProperty("message").GetString(), Is.EqualTo("second"));
    Assert.That(post.Message, Is.EqualTo("second"));
    Assert.That(peer.BatchRequests, Is.EqualTo(2));
  }

  [Test]
  public async Task ConcurrentSerializationKeepsTheValuesCapturedForEachRequest()
  {
    using var peer = new SerializationPeer();
    var source = new TradePostSource();
    var post = new TradePost(peer.Add(source));
    var held = peer.HoldNextCapture();
    var first = post.SerializeAsAsync<IMessage>();
    var pending = await held.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      source.RawMessage = "second";
      var second = await post.SerializeAsAsync<IMessage>().WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(second.Message, Is.EqualTo("second"));
      Assert.That(first.IsCompleted, Is.False);
    }
    finally
    {
      pending.Complete();
    }
    Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(5))).Message, Is.EqualTo("first"));
    Assert.That(post.Message, Is.EqualTo("second"));
    Assert.That(peer.BatchRequests, Is.EqualTo(2));
  }

  [Test]
  public async Task ConcurrentSerializationKeepsCapturedValuesWhenOperationsCompleteOutOfOrder()
  {
    using var peer = new SerializationPeer();
    var source = new TradePostSource();
    var post = new ObservedTradePost(peer.Add(source));
    var heldFirst = peer.HoldNextRead("Wanted");
    var first = Task.Run(() => post.SerializeAsAsync<IObservedMessage>());
    var pendingFirst = await heldFirst.WaitAsync(TimeSpan.FromSeconds(5));
    SerializationPeer.PendingCapture? pendingSecond = null;
    Task<IObservedMessage>? second = null;
    try
    {
      source.RawMessage = "second";
      var heldSecond = peer.HoldNextRead("Wanted");
      second = Task.Run(() => post.SerializeAsAsync<IObservedMessage>());
      pendingSecond = await heldSecond.WaitAsync(TimeSpan.FromSeconds(5));
      pendingFirst.Complete();
      var completed = await first.WaitAsync(TimeSpan.FromSeconds(5));
      Assert.That(completed.Message, Is.EqualTo("first"));
      Assert.That(completed.MessageAfterReadingWanted, Is.EqualTo("first"));
      Assert.That(second.IsCompleted, Is.False);
    }
    finally
    {
      pendingFirst.Complete();
      pendingSecond?.Complete();
    }
    var later = await second!.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(later.Message, Is.EqualTo("second"));
    Assert.That(later.MessageAfterReadingWanted, Is.EqualTo("second"));
    Assert.That(post.Message, Is.EqualTo("second"));
  }

  [Test]
  public void PartiallyConsumedCollectionCanBeDisposedAfterItsConnectionCloses()
  {
    IEnumerator<IPost> iterator;
    using (var peer = new SerializationPeer())
    {
      var sources = new[]
      {
        new TradePostSource { RawMessage = "first" },
        new TradePostSource { RawMessage = "second" }
      };
      iterator = DLRWrapper.SerializeDroAs<IPost, TradePost>(peer.Add(sources)).GetEnumerator();
      Assert.That(iterator.MoveNext(), Is.True);
      Assert.That(iterator.Current.Message, Is.EqualTo("first"));
    }
    Assert.DoesNotThrow(iterator.Dispose);
  }

  [Test]
  public void CollectionSerializationReadsFallbackMembersOnlyForConsumedRows()
  {
    using var peer = new SerializationPeer();
    var first = new TradePostSource { RawMessage = "first" };
    var second = new TradePostSource { RawMessage = "second" };
    var rows = DLRWrapper.SerializeDroAs<IPost, TradePost>(peer.Add(new[] { first, second }));
    Assert.That(first.WantedReads, Is.Zero);
    Assert.That(second.WantedReads, Is.Zero);
    using var iterator = rows.GetEnumerator();
    Assert.That(iterator.MoveNext(), Is.True);
    Assert.That(iterator.Current.Message, Is.EqualTo("first"));
    Assert.That(first.WantedReads, Is.EqualTo(1));
    Assert.That(second.WantedReads, Is.Zero);
    Assert.That(iterator.MoveNext(), Is.True);
    Assert.That(iterator.Current.Message, Is.EqualTo("second"));
    Assert.That(second.WantedReads, Is.EqualTo(1));
  }
}
