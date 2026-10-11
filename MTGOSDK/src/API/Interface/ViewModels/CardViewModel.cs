/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Batching;
using MTGOSDK.Core.Remoting.Interop.Interactions.Batching;


namespace MTGOSDK.API.Interface.ViewModels;

/// <summary>
/// Captures the screen geometry and drawing order of native card views.
/// </summary>
public static class CardViewModel
{
  /// <summary>
  /// Reads screen bounds and drawing order for cards with visible views.
  /// </summary>
  /// <remarks>
  /// Captures current views and transformed corners in one client UI-thread batch.
  /// Failed rows and invisible views are omitted. Compare drawing order only for
  /// cards sharing a panel index in this result. A view can move after capture.
  /// </remarks>
  public static IReadOnlyDictionary<int, Position> GetPositions(
    DuelSceneViewModel scene, IEnumerable<int> cardIds)
  {
    ArgumentNullException.ThrowIfNull(scene);
    ArgumentNullException.ThrowIfNull(cardIds);

    int[] ids = cardIds.Where(id => id > 0).Distinct().ToArray();
    var positions = new Dictionary<int, Position>();
    if (ids.Length == 0) return positions;

    var batch = CreateBatch((object)DLRWrapper.Unbind(scene), ids);

    // Build pixel bounds from the detached coordinates returned by the batch.
    foreach (var row in batch.Execute(RemoteClient.client).Rows)
    {
      if (row.Error is not null
        || !row.Values["matches"].GetScalar<bool>()
        || row.Values["visible"].Kind == RemoteBatchValueKind.Null
        || !row.Values["visible"].GetScalar<bool>())
        continue;

      var corners = new[] { "topLeft", "topRight", "bottomLeft", "bottomRight" }
        .Select(name => row.Values[name].Members)
        .Select(point => (
          X: point["X"].GetScalar<double>(),
          Y: point["Y"].GetScalar<double>()))
        .ToArray();
      double left = corners.Min(point => point.X);
      double top = corners.Min(point => point.Y);
      double right = corners.Max(point => point.X);
      double bottom = corners.Max(point => point.Y);
      int x = checked((int)Math.Floor(left));
      int y = checked((int)Math.Floor(top));
      int width = checked((int)Math.Ceiling(right)) - x;
      int height = checked((int)Math.Ceiling(bottom)) - y;
      if (width > 0 && height > 0)
        positions[ids[row.SourceIndex]] = new(
          new(x, y, width, height),
          row.Values["panelIndex"].GetScalar<int>(),
          row.Values["zIndex"].GetScalar<int>(),
          row.Values["childIndex"].GetScalar<int>());
    }

    return positions;
  }

  private static RemoteBatch CreateBatch(object scene, int[] ids)
  {
    var batch = new RemoteBatch { ForceUIThread = true };
    var game = batch.Read(batch.Input(scene), "Game");
    var zero = batch.Input(0d);
    var panels = batch.Create(s_panelListConstructor);
    batch.ForEach(batch.Input(ids), row =>
    {
      // Resolve the view currently associated with this card.
      var card = row.Call(game, s_findGameCard, row.Item);
      var viewModel = row.Read(card, "Tag.CardViewModel");
      var view = row.Read(viewModel, "m_battlefieldPositionView");
      row.Output("matches", row.CallStatic(s_referenceEquals,
        viewModel, row.Read(view, "DataContext")));
      row.Output("visible", row.Read(view, "IsVisible"));

      // MTGO applies drawing order to the container around Card_View. The
      // request-local list identifies cards that share the same parent panel.
      var container = row.CallStatic(s_getParent, view);
      var panel = row.CallStatic(s_getParent, container);
      row.Call(panels, s_panelListAdd, panel);
      row.Output("panelIndex", row.Call(panels, s_panelListIndexOf, panel));
      row.Output("zIndex", row.CallStatic(s_getZIndex, container));
      row.Output("childIndex",
        row.Call(row.Read(panel, "Children"), s_childIndexOf, container));

      // Transform all four corners to include rotation and ancestor transforms.
      var width = row.Read(view, "ActualWidth");
      var height = row.Read(view, "ActualHeight");
      foreach (var (name, x, y) in new[]
      {
        ("topLeft",     zero,  zero),
        ("topRight",    width, zero),
        ("bottomLeft",  zero,  height),
        ("bottomRight", width, height)
      })
      {
        var point = row.Create(s_pointConstructor, x, y);
        row.Output(name, row.Call(view, s_pointToScreen, point), s_pointProjection);
      }
    });

    return batch;
  }

  //
  // Captured geometry
  //

  /// <summary>
  /// A card view's screen bounds and applied drawing order within its panel.
  /// </summary>
  /// <remarks>
  /// Compare drawing order only for cards sharing a panel index in the same
  /// <see cref="GetPositions"/> result.
  /// </remarks>
  /// <param name="Bounds">Bounds enclosing the transformed view.</param>
  /// <param name="PanelIndex">Identifies the panel within one GetPositions result.</param>
  /// <param name="ZIndex">Higher values are drawn in front of lower values.</param>
  /// <param name="ChildIndex">Later children are drawn in front when ZIndex is equal.</param>
  public sealed record Position(
    ScreenBounds Bounds,
    int PanelIndex,
    int ZIndex,
    int ChildIndex
  );

  /// <summary>
  /// An axis-aligned rectangle enclosing a transformed view in screen pixels.
  /// </summary>
  /// <param name="X">Left edge of the screen rectangle.</param>
  /// <param name="Y">Top edge of the screen rectangle.</param>
  /// <param name="Width">Width of the screen rectangle.</param>
  /// <param name="Height">Height of the screen rectangle.</param>
  public sealed record ScreenBounds(
    int X,
    int Y,
    int Width,
    int Height
  );

  //
  // Native card lookup
  //

  // MTGO reference types are merged into the SDK package. Retain the native
  // declaring type and assembly so the method binds to the client's game.
  private static readonly RemoteBatchMethod s_findGameCard = new(
    new("WotC.MtGO.Client.Model.Play.InProgressGameEvent.Game",
        "WotC.MtGO.Client.Model.Play"),
    "FindGameCard", RemoteBatchType.From(typeof(int)));

  //
  // WPF coordinates and drawing order
  //

  private static readonly RemoteBatchMethod s_getParent =
    Method(typeof(VisualTreeHelper), nameof(VisualTreeHelper.GetParent),
      typeof(DependencyObject));

  private static readonly RemoteBatchMethod s_getZIndex =
    Method(typeof(Panel), nameof(Panel.GetZIndex), typeof(UIElement));

  private static readonly RemoteBatchMethod s_childIndexOf =
    Method(typeof(UIElementCollection), nameof(UIElementCollection.IndexOf),
      typeof(UIElement));

  private static readonly RemoteBatchMethod s_pointConstructor =
    Method(typeof(Point), ".ctor", typeof(double), typeof(double));

  private static readonly RemoteBatchMethod s_pointToScreen =
    Method(typeof(Visual), nameof(Visual.PointToScreen), typeof(Point));

  private static readonly RemoteBatchProjection s_pointProjection = RemoteBatchProjection.Object(
    (nameof(Point.X), nameof(Point.X), RemoteBatchProjection.Value()),
    (nameof(Point.Y), nameof(Point.Y), RemoteBatchProjection.Value()));

  //
  // Request-local panel identity
  //

  private static readonly RemoteBatchMethod s_referenceEquals =
    Method(typeof(object), nameof(object.ReferenceEquals),
      typeof(object), typeof(object));

  private static readonly RemoteBatchMethod s_panelListConstructor =
    Method(typeof(ArrayList), ".ctor");

  private static readonly RemoteBatchMethod s_panelListAdd =
    Method(typeof(ArrayList), nameof(ArrayList.Add), typeof(object));

  private static readonly RemoteBatchMethod s_panelListIndexOf =
    Method(typeof(ArrayList), nameof(ArrayList.IndexOf), typeof(object));

  private static RemoteBatchMethod Method(
    Type declaringType, string name, params Type[] parameters) =>
    new(RemoteBatchType.From(declaringType), name,
      parameters.Select(RemoteBatchType.From).ToArray());
}
