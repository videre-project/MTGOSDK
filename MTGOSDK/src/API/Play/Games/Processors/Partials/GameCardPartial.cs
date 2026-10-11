/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;

using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

using NativeAbility = WotC.MtGO.Client.Model.MagicCardAbility;
using NativeProperty = WotC.MtGO.Client.Model.MagicProperty;

namespace MTGOSDK.API.Play.Games.Processors.Partials
{
  /// <summary>
  /// A local adapter over a captured native card and SDK references.
  /// </summary>
  /// <remarks>
  /// Native state remains unchanged. Processors add target information
  /// and combat links locally before the snapshot is published.
  /// </remarks>
  public class GameCardPartial : DynamicObject
  {
    private readonly GameReferenceResolver _references;
    private readonly RemoteBatchObject _snapshot;
    private readonly RemoteBatchObject _members;
    private readonly RemoteBatchObject _propertyValues;
    private readonly CardCounter[] _counters;
    private readonly IReadOnlyDictionary<NativeAbility, RemoteBatchObject> _abilities;
    private readonly GameCardAssociation[] _associations;
    private int[]? _actionTargetIds;

    public GameCardPartial(PropertyContainer properties, dynamic game, RemoteBatchObject snapshot)
      : this(GameReferenceResolver.ForGame((object?)game), properties, snapshot) { }

    internal GameCardPartial(GameReferenceResolver references,
      PropertyContainer properties, RemoteBatchObject snapshot)
    {
      _references = references;
      Properties = properties ?? throw new ArgumentNullException(nameof(properties));
      _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
      _members = snapshot.Get<RemoteBatchObject>("members")
        ?? throw new ArgumentException("The snapshot has no native card members.", nameof(snapshot));
      // Freeze scalar property reads independently of processor backfills.
      // IDs are numeric so properties unknown to the SDK enum remain accessible.
      _propertyValues = new(properties.AllProperties
        .Where(pair => pair.Value is not PropertyContainer)
        .ToDictionary(pair => ((uint)pair.Key).ToString(CultureInfo.InvariantCulture),
          pair => (object?)pair.Value));
      _counters = _members.GetDictionary<CardCounter, int>("Counters")
        .SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToArray();
      _abilities = _members.GetDictionary<NativeAbility, RemoteBatchObject>("Abilities");
      _associations = snapshot.GetSequence<RemoteBatchObject>("associations")
        .Select(value => new GameCardAssociation(value)).ToArray();
      TargetInformation = _members.Get<string>("TargetInformation");
    }

    internal ulong? LogicalAddress => _members.LogicalAddress;

    public PropertyContainer Properties { get; private set; }

    /// <summary>
    /// Whether this adapter contains captured native card state.
    /// </summary>
    public bool HasResolvedState { get; private init; } = true;

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
      result = binder.Name switch
      {
        "GameInterface" => _references.RawGame,

        nameof(GameCard.TypeLine)   => _snapshot.Get<string>("typeLine"),
        nameof(GameCard.Definition) => _snapshot.Get<RemoteBatchObject?>("definition"),

        nameof(GameCard.Owner)      => GetOwnerWrapper(),
        nameof(GameCard.Controller) => GetControllerWrapper(),
        nameof(GameCard.Protector)  => GetProtectorWrapper(),
        nameof(GameCard.Zone) or nameof(GameCard.ActualZone) =>
          GetIntegerProperty((NativeProperty)(uint)MagicProperty.ZONE, -1) is int zoneId && zoneId >= 0
            ? _references.ResolveZoneObject((uint)zoneId, OwnerIndex)
            : null,
        nameof(GameCard.PreviousZone) =>
          _snapshot.Get<int>("previousZone") >= 0
            ? _references.ResolveZoneObject((uint)_snapshot.Get<int>("previousZone"), OwnerIndex)
            : null,

        nameof(GameCard.Counters)     => _counters,
        nameof(GameCard.Abilities)    => _abilities,
        nameof(GameCard.Associations) => Associations,
        nameof(TargetInformation)    => TargetInformation,
        nameof(GameCard.AttackingOrders) =>
          AttackingOrders.OrderBy(order => order.Order)
            .Select(order => order.Target).ToArray(),
        nameof(GameCard.BlockingOrders) =>
          BlockingOrders.OrderBy(order => order.Order)
            .Select(order => order.Target).ToArray(),

        "Actions" or "PendingTargets" or "OtherFace" => null,
        _ => _members.Get<object?>(binder.Name)
      };
      return true;
    }

    public int OwnerIndex => _members.Get<int?>("Owner") ?? -1;
    public int ControllerIndex => _members.Get<int?>("Controller") ?? -1;

    public GameZone? GetZoneWrapper() =>
      GetIntegerProperty((NativeProperty)(uint)MagicProperty.ZONE, -1) is int id && id >= 0
        ? _references.ResolveZone((uint)id, OwnerIndex)
        : null;

    public GameZone? GetPreviousZoneWrapper()
    {
      int id = _snapshot.Get<int>("previousZone");
      return id >= 0 ? _references.ResolveZone((uint)id, OwnerIndex) : null;
    }

    public GamePlayer GetOwnerWrapper() =>
      _references.ResolvePlayer(OwnerIndex)!;

    public GamePlayer GetControllerWrapper() =>
      _references.ResolvePlayer(ControllerIndex)!;

    public GamePlayer? GetProtectorWrapper() =>
      _references.ResolvePlayer(_members.Get<int?>("Protector") ?? -1);

    internal IEnumerable<GameCardAssociation> Associations => _actionTargetIds == null
      ? _associations
      : _associations.Where(value => value.Association != CardAssociation.ActionTarget)
        .Concat(_actionTargetIds.Select(id => new GameCardAssociation(CardAssociation.ActionTarget, id)));

    public Dictionary<string, List<int>> ResolveAssociations() => Associations
      .GroupBy(value => value.Association.ToString())
      .ToDictionary(group => group.Key, group => group.Select(value => value.TargetId).ToList());

    internal string TargetInformation { get; set; }
    internal void BackfillTargets(IReadOnlyList<int> ids) => _actionTargetIds = ids.ToArray();
    internal List<OrderedCombatParticipant> AttackingOrders { get; private set; } = new();
    internal List<OrderedCombatParticipant> BlockingOrders { get; private set; } = new();

    /// <summary>
    /// Copies processor-owned enrichment for the next gameplay snapshot.
    /// </summary>
    /// <remarks>
    /// Captured native values remain shared. Processors only write the
    /// local top-level properties, combat lists, and target enrichment.
    /// </remarks>
    internal GameCardPartial CopyForProcessing()
    {
      var copy = (GameCardPartial)MemberwiseClone();
      copy.Properties = new PropertyContainer(new(Properties.AllProperties), Properties.PropertyNames);
      copy.AttackingOrders = new(AttackingOrders);
      copy.BlockingOrders = new(BlockingOrders);
      return copy;
    }

    private static IEnumerable<(int Order, int TargetId)> CombatValues(
      IEnumerable<OrderedCombatParticipant> orders) => orders
        .OrderBy(order => order.Order).Select(order => (order.Order, order.Target.Id));

    internal int[] GetAttackingOrderIds() =>
      CombatValues(AttackingOrders).Select(value => value.TargetId).ToArray();

    internal int[] GetBlockingOrderIds() =>
      CombatValues(BlockingOrders).Select(value => value.TargetId).ToArray();

    public override bool Equals(object? obj) => obj is GameCardPartial other
      && _snapshot.Equals(other._snapshot) && _propertyValues.Equals(other._propertyValues)
      && HasResolvedState == other.HasResolvedState
      && TargetInformation == other.TargetInformation
      && (_actionTargetIds == null
            ? other._actionTargetIds == null
            : other._actionTargetIds != null && _actionTargetIds.SequenceEqual(other._actionTargetIds))
      && CombatValues(AttackingOrders).SequenceEqual(CombatValues(other.AttackingOrders))
      && CombatValues(BlockingOrders).SequenceEqual(CombatValues(other.BlockingOrders));

    public override int GetHashCode()
    {
      var hash = new HashCode();
      hash.Add(_snapshot);
      hash.Add(_propertyValues);
      hash.Add(HasResolvedState);
      hash.Add(TargetInformation);
      hash.Add(_actionTargetIds != null);
      if (_actionTargetIds != null)
      {
        foreach (var id in _actionTargetIds)
          hash.Add(id);
      }
      foreach (var order in CombatValues(AttackingOrders))
        hash.Add(order);
      foreach (var order in CombatValues(BlockingOrders))
        hash.Add(order);
      return hash.ToHashCode();
    }

    public static bool operator ==(GameCardPartial? left, GameCardPartial? right) => Equals(left, right);
    public static bool operator !=(GameCardPartial? left, GameCardPartial? right) => !Equals(left, right);

    // Match native PropertyContainer access semantics over detached scalar values.
    public int GetIntegerProperty(NativeProperty property, int defaultValue = 0)
    {
      _propertyValues.Members.TryGetValue(((uint)property).ToString(CultureInfo.InvariantCulture), out var value);
      return value is int number ? number
        : value is string text && int.TryParse(text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out number) ? number : defaultValue;
    }

    public bool GetBooleanProperty(NativeProperty property) => GetIntegerProperty(property) >= 1;

    public string? GetStringProperty(NativeProperty property, string? defaultValue = null)
    {
      if (!_propertyValues.Members.TryGetValue(((uint)property).ToString(CultureInfo.InvariantCulture), out var value))
        return defaultValue;
      return value is int number ? number.ToString(CultureInfo.InvariantCulture) : (string?)value;
    }

    /// <summary>
    /// Creates display identity without inventing native card state.
    /// </summary>
    internal static GameCardPartial CreatePlaceholder(
      GameReferenceResolver references, PropertyContainer properties)
    {
      var identity = new RemoteBatchObject(new Dictionary<string, object?>
      {
        ["Id"]                = properties[MagicProperty.THINGNUMBER] ?? 0,
        ["Name"]              = properties[MagicProperty.CARDNAME_STRING] ?? string.Empty,
        ["SourceId"]          = properties[MagicProperty.SRC_THING_ID] ?? -1,
        ["Owner"]             = properties[MagicProperty.OWNER],
        ["Controller"]        = properties[MagicProperty.CONTROLLER],
        ["Protector"]         = null,
        ["TargetInformation"] = string.Empty,
        ["Counters"]          = Array.Empty<KeyValuePair<object?, object?>>(),
        ["Abilities"]         = Array.Empty<KeyValuePair<object?, object?>>()
      });
      return new(references, properties, new RemoteBatchObject(new Dictionary<string, object?>
      {
        ["members"]      = identity,
        ["previousZone"] = -1,
        ["typeLine"]     = string.Empty,
        ["definition"]   = null,
        ["associations"] = Array.Empty<object>()
      })) { HasResolvedState = false };
    }
  }
}

namespace MTGOSDK.API.Play.Games.Processors
{
  /// <summary>
  /// Captures completed native cards and their gameplay-message properties by value.
  /// Native member identities and snapshot acceptance belong to this game layer.
  /// </summary>
  internal static class GameCardCapture
  {
    internal sealed record Snapshot(
      int SourceIndex, PropertyContainer Properties, RemoteBatchObject Values);

    private static readonly RemoteBatchType s_containerType = new(
      "WotC.MtGO.Client.Model.Core.PropertyContainer", "WotC.MtGO.Client.Model.Core");
    private static readonly RemoteBatchMethod s_constructor = new(s_containerType, ".ctor");
    private static readonly RemoteBatchMethod s_copyFrom = new(s_containerType, "CopyFrom", s_containerType);
    private static readonly RemoteBatchMethod s_typeLine = new(
      new("Mtgo.Shared.Parser.Attributes.Helpers.MagicPropertyHelper", "Mtgo.Shared.Parser.Attributes"),
      "ConstructCardTypeText",
      new RemoteBatchType("WotC.MtGO.Client.Model.IPropertyHolder", "Mtgo.Shared.Parser.Attributes"));

    private static readonly RemoteBatchType s_gameType = new(
      "WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game", "WotC.MtGO.Client.Model.Play");
    private static readonly RemoteBatchType s_propertyType = new(
      "WotC.MtGO.Client.Model.MagicProperty", "Mtgo.Shared.Parser.Attributes");
    private static readonly RemoteBatchMethod s_thingNumber = new(
      s_containerType, "GetIntegerProperty", s_propertyType, RemoteBatchType.From(typeof(int)));
    private static readonly RemoteBatchMethod s_findCard = new(
      s_gameType, "ResolveDigitalThingAsGameCard", RemoteBatchType.From(typeof(int)));
    private static readonly RemoteBatchMethod s_getZone = new(
      s_gameType, "GetCardZone",
      new RemoteBatchType("WotC.MtGO.Client.Model.Play.CardZone", "WotC.MtGO.Client.Model.Reference"),
      new RemoteBatchType("WotC.MtGO.Client.Model.Play.IGamePlayer", "WotC.MtGO.Client.Model.Reference"));

    /// <summary>
    /// Adds card and player values to the existing gameplay postfix capture.
    /// Values are serialized before MTGO can process the next gameplay message.
    /// </summary>
    internal static void AppendGameplayCapture(
      RemoteBatch batch, RemoteBatchSlot message, RemoteBatchSlot game)
    {
      DefinePropertiesProjection(batch);
      batch.DefineProjection("definition", DefinitionProjection());
      var thingNumber = batch.Input((uint)MagicProperty.THINGNUMBER);
      var missingId = batch.Input(-1);

      batch.ForEach(batch.Read(message, "ThingElements"), row =>
      {
        var properties = row.Read("Properties");
        var id = row.Call(properties, s_thingNumber, thingNumber, missingId);
        // Native lookup returns null for players; their message properties are
        // still captured. Read and output projections preserve successful nulls.
        var card = row.Call(game, s_findCard, id);
        AppendCardOutputs(row, card, properties, row.Read("FromZone"));
      });

      // Reveals are native zone objects, not ThingElements. Their IDs can all be
      // -1, so capture every object with its logical address at the same boundary.
      var revealed = batch.Input((int)CardZone.Revealed);
      var noPreviousZone = batch.Input(-1);
      batch.ForEach(batch.Read(game, "PlayersInServerOrder"), player =>
      {
        var zone = player.Call(game, s_getZone, revealed, player.Item);
        player.ForEach(zone, row =>
          AppendCardOutputs(row, row.Item, row.Read("Properties"), noPreviousZone));
      });

      // Source indices repeat across collections. Record the operation identity
      // so decoding can distinguish reveal rows even when a row has failed.
      int collectionId = batch.ToRequest().Operations.Last().Id;
      batch.Output("revealedZoneCollectionId", batch.Input(collectionId));
    }

    private static void AppendCardOutputs(RemoteBatchScope row, RemoteBatchSlot card,
      RemoteBatchSlot properties, RemoteBatchSlot previousZone)
    {
      row.Output("properties",   properties, RemoteBatchProjection.Named("properties"));
      row.Output("previousZone", previousZone);
      row.Output("typeLine",     row.CallStatic(s_typeLine, properties));
      row.Output("associations", row.Read(card, "Associations"), AssociationProjection());
      row.Output("members",      card, CardProjection());
      row.Output("definition",   row.Read(card, "Definition"), RemoteBatchProjection.Named("definition"));
    }

    private static RemoteBatchProjection CardProjection() => RemoteBatchProjection.ObjectScalars(
      new("WotC.MtGO.Client.Model.Play.IGameCard", "WotC.MtGO.Client.Model.Reference"),
      ("Owner",      "Owner.Id",      RemoteBatchProjection.Value()),
      ("Controller", "Controller.Id", RemoteBatchProjection.Value()),
      ("Protector",  "Protector.Id",  RemoteBatchProjection.Value()),
      ("Counters",   "Counters",      RemoteBatchProjection.Dictionary(
        RemoteBatchProjection.Value(), RemoteBatchProjection.Value())),
      ("Abilities",  "Abilities",     RemoteBatchProjection.Dictionary(RemoteBatchProjection.Value(),
        RemoteBatchProjection.ObjectScalars(new("WotC.MtGO.Client.Model.ICardAbility",
          "WotC.MtGO.Client.Model.Reference"))))).WithLogicalAddress();

    private static RemoteBatchProjection AssociationProjection() => RemoteBatchProjection.Sequence(
      RemoteBatchProjection.ObjectScalars(
        new("WotC.MtGO.Client.Model.Play.IGameCardAssociation", "WotC.MtGO.Client.Model.Reference"),
        ("AssociatedTarget", "AssociatedTarget", RemoteBatchProjection.Object(
          ("Id", "Id", RemoteBatchProjection.Value())))));

    private static RemoteBatchProjection DefinitionProjection() => RemoteBatchProjection.ObjectScalars(
      new("WotC.MtGO.Client.Model.ICardDefinition", "WotC.MtGO.Client.Model.Reference"),
      ("Subtypes",    "Subtypes",       RemoteBatchProjection.Sequence(RemoteBatchProjection.Value())),
      ("Rarity",      "Rarity",         RemoteBatchProjection.Object(
        ("Name", "Name", RemoteBatchProjection.Value()))),
      ("CardSet",     "CardSet",        RemoteBatchProjection.Object(
        ("Code",        "Code",           RemoteBatchProjection.Value()),
        ("Name",        "Name",           RemoteBatchProjection.Value()),
        ("ReleaseDate", "ReleaseDate",    RemoteBatchProjection.Value()),
        ("Age",         "Age",            RemoteBatchProjection.Value()),
        ("Type",        "Type.EnumValue", RemoteBatchProjection.Value()))));

    /// <summary>
    /// Captures a native container for callers without a gameplay message.
    /// </summary>
    internal static PropertyContainer CaptureProperties(object container)
    {
      var batch = BuildPropertiesBatch(container);
      var result = batch.Execute(RemoteClient.@client);
      return PropertyContainer.FromBatchValue(result.Values["properties"]);
    }

    internal static RemoteBatch BuildPropertiesBatch(object container)
    {
      var batch = new RemoteBatch();
      DefinePropertiesProjection(batch);
      var copy = batch.Create(s_constructor);
      batch.Call(copy, s_copyFrom, batch.Input(container));
      batch.Output("properties", copy, RemoteBatchProjection.Named("properties"));
      return batch;
    }

    private static void DefinePropertiesProjection(RemoteBatch batch) =>
      batch.DefineProjection("properties", RemoteBatchProjection.Object(
        ("integers", "IntegerProperties", RemoteBatchProjection.Dictionary(
          RemoteBatchProjection.Value(), RemoteBatchProjection.Value())),
        ("strings",  "StringProperties",  RemoteBatchProjection.Dictionary(
          RemoteBatchProjection.Value(), RemoteBatchProjection.Value())),
        ("children", "SubProperties",     RemoteBatchProjection.Dictionary(
          RemoteBatchProjection.Value(), RemoteBatchProjection.Named("properties")))));

    internal static IReadOnlyList<Snapshot> Decode(
      RemoteBatchResult result, int[] cardIndices, int[] playerIndices)
    {
      var selectedIndices = cardIndices.Concat(playerIndices).ToArray();
      var cards = new HashSet<int>(cardIndices);
      var expected = new HashSet<int>(selectedIndices);
      if (expected.Count != selectedIndices.Length)
        throw new ArgumentException("Card and player selections must not overlap or repeat.");
      int revealedCollection = result.Values["revealedZoneCollectionId"].GetScalar<int>();
      var rows = result.Rows.Where(row => row.CollectionOperationId != revealedCollection
        && expected.Contains(row.SourceIndex)).ToArray();
      if (rows.Length != expected.Count)
        throw new InvalidOperationException("Card capture did not return the requested rows.");

      var captured = new List<Snapshot>(rows.Length);
      foreach (var row in rows)
      {
        if (!expected.Remove(row.SourceIndex))
          throw new InvalidOperationException($"Unexpected card capture source index {row.SourceIndex}.");
        captured.Add(DecodeRow(row, cards.Contains(row.SourceIndex)));
      }
      return captured;
    }

    /// <summary>
    /// Decodes the native revealed zones without using server card IDs as identity.
    /// A failed zone or card capture prevents accepting a partial reveal list.
    /// </summary>
    internal static IReadOnlyList<Snapshot> DecodeRevealed(RemoteBatchResult result)
    {
      int collectionId = result.Values["revealedZoneCollectionId"].GetScalar<int>();
      var captured = new List<Snapshot>();
      foreach (var zone in result.Rows.Where(row => row.CollectionOperationId == collectionId))
      {
        CheckRow(zone);
        foreach (var row in zone.Rows)
          captured.Add(DecodeRow(row, true));
      }
      return captured;
    }

    private static Snapshot DecodeRow(RemoteBatchRow row, bool isCard)
    {
      CheckRow(row);
      var properties = PropertyContainer.FromBatchValue(row.Values["properties"]);
      var values = new RemoteBatchObject(row.Values
        .Where(pair => pair.Key != "properties")
        .ToDictionary(pair => pair.Key, pair => RemoteBatchObject.FromValue(pair.Value)));
      if (isCard)
      {
        if (values.Get<RemoteBatchObject>("members") == null || values.Get<string>("typeLine") == null)
          throw new InvalidOperationException($"Incomplete card capture for source {row.SourceIndex}.");
        _ = values.Get<RemoteBatchObject?>("definition");
      }
      return new(row.SourceIndex, properties, values);
    }

    private static void CheckRow(RemoteBatchRow row)
    {
      if (row.Error is { } error)
        throw new InvalidOperationException(
          $"Card capture source {row.SourceIndex}, operation {error.OperationId}" +
          $" ({error.OutputPath ?? "reflection"}) failed: {error.ExceptionType}: {error.Message}");
    }
  }
}
