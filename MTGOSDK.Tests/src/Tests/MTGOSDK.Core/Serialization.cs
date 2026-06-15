/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.Core.Reflection.Attributes;
using MTGOSDK.Core.Reflection.Serialization;


namespace MTGOSDK.Tests.MTGOSDK_Core;

public class Serialization
{
  public interface ISerializableProbe
  {
    string PublicValue { get; }
    string HiddenValue { get; }
  }

  private sealed class SerializableProbe : SerializableBase
  {
    public string PublicValue => "public";

    [NonSerializable]
    public string HiddenValue => "hidden";
  }

  [Test]
  public void Test_NonSerializable_DefaultSerialization_CanBeOverriddenByInterface()
  {
    var probe = new SerializableProbe();

    var defaultSerialization =
      (IDictionary<string, object>)probe.ToSerializable();
    Assert.That(defaultSerialization.ContainsKey(nameof(SerializableProbe.PublicValue)),
                Is.True);
    Assert.That(defaultSerialization.ContainsKey(nameof(SerializableProbe.HiddenValue)),
                Is.False);

    var projected = probe.SerializeAs<ISerializableProbe>();
    Assert.That(projected.PublicValue, Is.EqualTo("public"));
    Assert.That(projected.HiddenValue, Is.EqualTo("hidden"));
  }
}
