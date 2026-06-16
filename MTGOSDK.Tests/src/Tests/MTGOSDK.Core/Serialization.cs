/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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

  [NonSerializable]
  private sealed class RemovedProbe
  {
  }

  [NonSerializable]
  private sealed class StringifiedProbe
  {
    public override string ToString() => "stringified";
  }

  private sealed class ContractProbe
  {
    [JsonPropertyName("explicit_hidden")]
    [NonSerializable]
    public string HiddenValue => "hidden";

    public RemovedProbe RemovedValue => new();

    public StringifiedProbe StringifiedValue => new();

    public List<StringifiedProbe> StringifiedValues => [];
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

  [Test]
  public void Test_NonSerializableJsonContract_ReportsRuntimeSerializationShape()
  {
    var directives = NonSerializableJsonContract
      .GetPropertyDirectives(typeof(ContractProbe))
      .ToDictionary(directive => directive.PropertyNames[0]);

    Assert.That(directives["explicit_hidden"].Action,
                Is.EqualTo(NonSerializableJsonPropertyAction.Remove));
    Assert.That(directives["explicit_hidden"].PropertyNames,
                Contains.Item(nameof(ContractProbe.HiddenValue)));
    Assert.That(directives[nameof(ContractProbe.RemovedValue)].Action,
                Is.EqualTo(NonSerializableJsonPropertyAction.Remove));
    Assert.That(directives[nameof(ContractProbe.StringifiedValue)].Action,
                Is.EqualTo(NonSerializableJsonPropertyAction.Stringify));
    Assert.That(directives[nameof(ContractProbe.StringifiedValues)].Action,
                Is.EqualTo(NonSerializableJsonPropertyAction.StringifyEnumerable));
  }
}
