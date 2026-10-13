/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Proxy;
using MTGOSDK.Core.Reflection.Serialization;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.UnitTests;

public class BatchSerialization
{
  [Test]
  public void TakeDoesNotReadPastTheRequestedItems()
  {
    var batch = new RemoteBatch();
    var source = batch.Input(ObjectOrRemoteAddress.FromToken(1, "source"));
    batch.Output("items", batch.Take(source, 2));
    var result = Execute(batch, Items());
    Assert.That(result.Values["items"].Items.Select(item => item.GetScalar<int>()),
      Is.EqualTo(new[] { 1, 2 }));

    static IEnumerable<int> Items()
    {
      yield return 1;
      yield return 2;
      throw new System.InvalidOperationException("Later items must not be read.");
    }
  }

  [TestCase(0, new int[0])]
  [TestCase(8, new[] { 1, 2 })]
  public void TakeAllowsEmptyAndShortCollections(int count, int[] expected)
  {
    var batch = new RemoteBatch();
    var source = batch.Input(new[] { 1, 2 });
    batch.Output("items", batch.Take(source, count));
    Assert.That(Execute(batch, null).Values["items"].Items.Select(item => item.GetScalar<int>()),
      Is.EqualTo(expected));
  }

  [Test]
  public void OptionalMembersDoNotDiscardSuccessfulMembers()
  {
    var projection = RemoteBatchProjection.Object(
      ("Name", "Name", RemoteBatchProjection.Value()),
      ("Missing", "Missing", RemoteBatchProjection.Value()),
      ("Broken", "Broken", RemoteBatchProjection.Value()));
    projection.Members["Missing"].IgnoreErrors = true;
    projection.Members["Broken"].IgnoreErrors = true;
    var batch = new RemoteBatch();
    batch.Output("item", batch.Input(ObjectOrRemoteAddress.FromToken(1, "source")), projection);
    var source = new Source();
    var result = Execute(batch, source);
    Assert.That(result.Values["item"].Members.Keys, Is.EquivalentTo(new[] { "Name" }));
    Assert.That(result.Values["item"].Members["Name"].GetScalar<string>(), Is.EqualTo("test"));

    projection.Members["Broken"].IgnoreErrors = false;
    var error = Assert.Catch<InvalidOperationException>(() => Execute(batch, source));
    Assert.That(error!.GetBaseException(), Is.SameAs(source.Failure));
  }

  [TestCase(State.Ready, 1, "Ready")]
  [TestCase((State)8, 8, "8")]
  public void InterfaceMaterializationPreservesEnumValuesAndSequences(
    State state, int number, string name)
  {
    var batch = new RemoteBatch();
    batch.Output("item", batch.Input(ObjectOrRemoteAddress.FromToken(1, "source")),
      RemoteBatchProjection.Object(
        ("State", "State", RemoteBatchProjection.Value()),
        ("Number", "State", RemoteBatchProjection.Value()),
        ("UnsignedNumber", "State", RemoteBatchProjection.Value()),
        ("StateName", "State", RemoteBatchProjection.Value()),
        ("Labels", "Labels", RemoteBatchProjection.Value()),
        ("EnumerableLabels", "Labels", RemoteBatchProjection.Value()),
        ("ReadOnlyLabels", "Labels", RemoteBatchProjection.Value())));
    var result = Execute(batch, new { State = state, Labels = new[] { "a", "b" } });
    var values = result.Values["item"].Members.ToDictionary(member => member.Key,
      member => RemoteBatchObject.FromValue(member.Value));
    var row = InterfaceProxyBuilder.Create<IRow>(values);
    Assert.That((int)row.State, Is.EqualTo(number));
    Assert.That(row.Number, Is.EqualTo(number));
    Assert.That(row.UnsignedNumber, Is.EqualTo((uint)number));
    Assert.That(row.StateName, Is.EqualTo(name));
    Assert.That(row.Labels, Is.EqualTo(new[] { "a", "b" }));
    Assert.That(row.EnumerableLabels, Is.EqualTo(new[] { "a", "b" }));
    Assert.That(row.ReadOnlyLabels, Is.EqualTo(new[] { "a", "b" }));
  }

  [Test]
  public void CachedNestedMembersDoNotReadTheirNativeParent()
  {
    var source = new NestedSource();
    dynamic proxy = new CachingRemoteProxy(source, new Dictionary<string, object?>
    {
      ["Child.Name"] = "captured"
    });
    dynamic child = proxy.Child;
    Assert.That((string)child.Name, Is.EqualTo("captured"));
    Assert.That(source.Reads, Is.Zero);
    Assert.That((int)child.Number, Is.EqualTo(42));
    Assert.That(source.Reads, Is.EqualTo(1));
  }

  [Test]
  public void CollectionSerializerPreservesItsObjectParameter()
  {
    var method = typeof(DLRWrapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
      .Single(method => method.Name == "SerializeDroAs" && method.GetGenericArguments().Length == 2);
    Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(object)));
  }

  public sealed class NestedSource
  {
    public int Reads { get; private set; }
    public NestedValue Child { get { Reads++; return new(); } }
  }

  public sealed class NestedValue
  {
    public string Name => "native";
    public int Number => 42;
  }

  private static RemoteBatchResult Execute(RemoteBatch batch, object? source) =>
    new BatchExecutor(identity => System.Type.GetType(identity.FullName)!,
      input => input.IsRemoteAddress ? source : PrimitivesEncoder.Decode(input.EncodedObject, input.Type))
      .Execute(batch.ToRequest());

  private sealed class Source
  {
    public string Name => "test";
    public InvalidOperationException Failure { get; } = new("Unavailable.");
    public int Broken => throw Failure;
  }

  public enum State { Ready = 1 }

  public interface IRow
  {
    State State { get; }
    int Number { get; }
    uint UnsignedNumber { get; }
    string StateName { get; }
    IList<string> Labels { get; }
    IEnumerable<string> EnumerableLabels { get; }
    IReadOnlyList<string> ReadOnlyLabels { get; }
  }
}
