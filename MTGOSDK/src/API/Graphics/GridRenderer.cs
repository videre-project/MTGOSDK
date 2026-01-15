/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

using Shiny.CardManager.Controls;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Win32.API;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Graphics;

/// <summary>
/// Represents metadata for a single card slot in the rendered grid.
/// </summary>
/// <param name="Index">The slot's position in the grid (0-based).</param>
/// <param name="CardId">The card's catalog ID.</param>
/// <param name="Name">The card's name.</param>
/// <param name="Quantity">Number of copies in this slot.</param>
public record SlotInfo(int Index, int CardId, string Name, int Quantity);

/// <summary>
/// Renders an entire deck as a grid image using MTGO's CardStackItemsSelector.
/// </summary>
public static class GridRenderer
{
  /// <summary>
  /// Aspect ratio matching MTGO's pile control defaults (5/7).
  /// </summary>
  private const double CardAspectRatio = 0.7142857142857143;

  /// <summary>
  /// Default card height in pixels.
  /// </summary>
  private const double DefaultCardHeight = 300.0;

  /// <summary>
  /// Default number of columns in the grid.
  /// </summary>
  private const int DefaultColumns = 5;

  // Cached remote objects for reuse across renders
  private static dynamic? s_cachedVM;
  private static dynamic? s_cachedSelector;
  private static dynamic? s_cachedPixelFormat;

  private static bool s_isInitialized = false;

  /// <summary>
  /// Gets the InstantRender property from ArtInFrameToBrushConverter.
  /// </summary>
  private static System.Reflection.PropertyInfo? GetInstantRenderProperty()
  {
    var converterType = RemoteClient.GetInstanceType(
      "Shiny.Card.Converters.ArtInFrameToBrushConverter");
    return converterType?.GetProperty("InstantRender");
  }

  /// <summary>
  /// Enables synchronous card art loading and returns the previous value.
  /// </summary>
  private static bool EnableInstantRender()
  {
    var prop = GetInstantRenderProperty();
    if (prop is null) return false;

    bool originalValue = (bool)prop.GetValue(null)!;
    prop.SetValue(null, true);
    return originalValue;
  }

  /// <summary>
  /// Restores the InstantRender property to a previous value.
  /// </summary>
  private static void RestoreInstantRender(bool originalValue)
  {
    var prop = GetInstantRenderProperty();
    prop?.SetValue(null, originalValue);
  }

  /// <summary>
  /// Initializes cached remote objects for grid rendering.
  /// Must be called within a UI thread scope.
  /// </summary>
  private static void EnsureRenderingInfrastructure()
  {
    if (s_isInitialized) return;

    if (!RemoteClient.IsInitialized)
    {
      throw new InvalidOperationException("RemoteClient is not initialized.");
    }

    // Create CardGroupingViewModel
    if (s_cachedVM is null)
    {
      s_cachedVM = RemoteClient.CreateInstance(
        "Shiny.CardManager.ViewModels.CardGroupingViewModel");
      RemoteClient.Disposed += (_, _) => ClearCache();

      // Create custom CardSortMode with Name + CardId for deterministic ordering
      // This ensures same-name cards (different printings) are consistently ordered
      var sortModeType = RemoteClient.GetInstanceType("Shiny.CardManager.CardSortMode");
      var sortKeyType = RemoteClient.GetInstanceType("Shiny.CardManager.CardSortKey");
      
      // Get CardSortKey.Name and CardSortKey.CardId static fields
      var nameKey = sortKeyType.GetField("Name", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
      var cardIdKey = sortKeyType.GetField("CardId",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
      
      // Create custom CardSortMode("pile:none;sort:Name,CardId")
      dynamic customSortMode = RemoteClient.CreateInstance(
        "Shiny.CardManager.CardSortMode", "pile:none;sort:Name,CardId");
      
      // Set PilingCriterion = CardSortKey.None
      var noneKey = sortKeyType.GetField("None",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
      customSortMode.PilingCriterion = noneKey;
      
      // Clear and populate SortingCriteria with Name + CardId
      // The constructor initializes with a default list, so we can modify it
      dynamic sortingCriteria = customSortMode.SortingCriteria;
      sortingCriteria.Clear();
      sortingCriteria.Add(nameKey);
      sortingCriteria.Add(cardIdKey);

      // Create CardViewLayout.Stack enum value
      var stackLayout = RemoteClient.CreateEnum(
        "Shiny.CardManager.CardViewLayout", "Stack");

      // Set layout to Stack and sort mode to custom Name+CardId
      s_cachedVM.SetLayout(stackLayout, false);
      s_cachedVM.SetSortMode(customSortMode, false, false);
    }

    // Create CardStackItemsSelector
    if (s_cachedSelector is null)
    {
      s_cachedSelector = RemoteClient.CreateInstance<CardStackItemsSelector>();

      // Hide scrollbars for rendering
      var scrollViewerType = RemoteClient.GetInstanceType(
        typeof(System.Windows.Controls.ScrollViewer).FullName!);
      var verticalVisibilityProp = scrollViewerType.GetField(
        "VerticalScrollBarVisibilityProperty",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
      var horizontalVisibilityProp = scrollViewerType.GetField(
        "HorizontalScrollBarVisibilityProperty",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
      var hiddenVisibility = RemoteClient.CreateEnum(
        "System.Windows.Controls.ScrollBarVisibility", "Hidden");

      if (verticalVisibilityProp != null)
        s_cachedSelector.SetValue(verticalVisibilityProp.GetValue(null), hiddenVisibility);
      if (horizontalVisibilityProp != null)
        s_cachedSelector.SetValue(horizontalVisibilityProp.GetValue(null), hiddenVisibility);

      // Pre-load control template
      s_cachedSelector.ApplyTemplate();
    }

    // Cache PixelFormat
    if (s_cachedPixelFormat is null)
    {
      var pixelFormatsType = RemoteClient.GetInstanceType(
        typeof(PixelFormats).FullName!);
      var pbgra32Prop = pixelFormatsType.GetProperty("Pbgra32");
      s_cachedPixelFormat = pbgra32Prop!.GetValue(null);
    }

    s_isInitialized = true;
  }

  /// <summary>
  /// Initializes the grid rendering infrastructure.
  /// Call this once before rendering to amortize setup cost.
  /// </summary>
  public static void Initialize()
  {
    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();
    }
  }

  /// <summary>
  /// Core rendering logic. Assumes caller is within UI thread scope.
  /// </summary>
  /// <returns>Tuple of (pixelData, width, height)</returns>
  private static (byte[] Pixels, int Width, int Height) RenderDeckCore(
    dynamic deckGrouping, int columns, double cardHeight)
  {
    // Configure card dimensions
    double cardWidth = cardHeight * CardAspectRatio;
    s_cachedSelector!.CardHeight = cardHeight;

    // Set the card grouping - this populates Slots automatically
    s_cachedVM!.SetCardGrouping(deckGrouping, true);

    // Set ItemsSource directly to VM.Slots (populated by SetCardGrouping)
    s_cachedSelector.ItemsSource = s_cachedVM.Slots;

    // Update layout before measuring
    s_cachedSelector.UpdateLayout();

    // Get the slot count for grid dimension calculations
    int slotCount = Cast<int>(s_cachedVM.Slots.Count);
    int rows = (slotCount + columns - 1) / columns;

    // Calculate expected dimensions
    double expectedWidth = columns * cardWidth;
    double expectedHeight = rows * cardHeight;

    // Create Size for remote Measure with finite constraints
    dynamic measureSize = RemoteClient.CreateInstance<Size>(
      expectedWidth, expectedHeight);

    // Force full layout realization
    s_cachedSelector.Measure(measureSize);

    // Get the actual desired size after measurement
    dynamic desiredSize = s_cachedSelector.DesiredSize;
    double actualWidth = Cast<double>(desiredSize.Width);
    double actualHeight = Cast<double>(desiredSize.Height);

    // Guard against infinite or NaN values
    if (double.IsInfinity(actualWidth) || double.IsNaN(actualWidth))
      actualWidth = expectedWidth;
    if (double.IsInfinity(actualHeight) || double.IsNaN(actualHeight))
      actualHeight = expectedHeight;

    dynamic arrangeRect = RemoteClient.CreateInstance<Rect>(
      0.0, 0.0, actualWidth, actualHeight);

    s_cachedSelector.Arrange(arrangeRect);

    // Create RenderTargetBitmap with actual dimensions
    dynamic renderTarget = RemoteClient.CreateInstance<RenderTargetBitmap>(
      (int)actualWidth,
      (int)actualHeight,
      96.0,
      96.0,
      s_cachedPixelFormat
    );

    // Render the selector to bitmap
    renderTarget.Render(s_cachedSelector);

    // Create WriteableBitmap to access pixel data
    dynamic writeableBitmap = RemoteClient.CreateInstance<WriteableBitmap>(
      renderTarget
    );

    // Read pixel data from remote process memory
    IntPtr backBuffer = Cast<IntPtr>(writeableBitmap.BackBuffer);
    int stride = writeableBitmap.BackBufferStride;
    int bufferSize = stride * (int)actualHeight;

    byte[] pixels = new byte[bufferSize];
    bool success = Kernel32.ReadProcessMemory(
      RemoteClient.ClientProcess.Handle,
      backBuffer,
      pixels,
      (nuint)bufferSize,
      out nuint _
    );

    if (!success)
      throw new InvalidOperationException(
        "Failed to read bitmap pixels from remote process memory.");

    return (pixels, (int)actualWidth, (int)actualHeight);
  }

  /// <summary>
  /// Renders a deck as a grid image.
  /// </summary>
  /// <param name="deck">The deck to render.</param>
  /// <param name="columns">Number of columns in the grid (default: 5).</param>
  /// <param name="cardHeight">Height of each card in pixels (default: 300).</param>
  /// <returns>Raw BGRA pixel data for the rendered grid.</returns>
  public static byte[] RenderDeckToPixels(
    Deck deck,
    int columns = DefaultColumns,
    double cardHeight = DefaultCardHeight)
  {
    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();

      bool originalInstantRender = EnableInstantRender();
      try
      {
        // Get the unbound IDeck/ICardGrouping from the Deck wrapper
        dynamic deckGrouping = Unbind(deck);
        var result = RenderDeckCore(deckGrouping, columns, cardHeight);
        return result.Item1;
      }
      finally
      {
        RestoreInstantRender(originalInstantRender);
      }
    }
  }

  /// <summary>
  /// Gets the dimensions of the rendered grid for a deck.
  /// </summary>
  /// <param name="cardCount">Number of cards in the deck.</param>
  /// <param name="columns">Number of columns.</param>
  /// <param name="cardHeight">Height of each card.</param>
  /// <returns>Tuple of (width, height) in pixels.</returns>
  public static (int Width, int Height) CalculateGridDimensions(
    int cardCount,
    int columns = DefaultColumns,
    double cardHeight = DefaultCardHeight)
  {
    double cardWidth = cardHeight * CardAspectRatio;
    int rows = (cardCount + columns - 1) / columns;

    int width = (int)(columns * cardWidth);
    int height = (int)(rows * cardHeight);

    return (width, height);
  }

  /// <summary>
  /// Saves rendered grid pixel data as a PNG file.
  /// </summary>
  /// <param name="pixelData">Raw BGRA pixel data from RenderDeckToPixels.</param>
  /// <param name="width">Width of the image.</param>
  /// <param name="height">Height of the image.</param>
  /// <param name="filePath">Output file path.</param>
  public static void SaveGridAsPng(
    byte[] pixelData,
    int width,
    int height,
    string filePath)
  {
    int stride = width * 4; // BGRA32

    var bitmap = new WriteableBitmap(
      width,
      height,
      96.0,
      96.0,
      PixelFormats.Bgra32,
      null
    );

    bitmap.Lock();
    try
    {
      Marshal.Copy(pixelData, 0, bitmap.BackBuffer, pixelData.Length);
      bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
    }
    finally
    {
      bitmap.Unlock();
    }

    using var fileStream = new FileStream(filePath, FileMode.Create);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    encoder.Save(fileStream);
  }

  /// <summary>
  /// Renders a deck as a grid and returns PNG bytes with dimensions.
  /// </summary>
  /// <param name="deck">The deck to render.</param>
  /// <param name="columns">Number of columns in the grid.</param>
  /// <param name="cardHeight">Height of each card in pixels.</param>
  /// <returns>Tuple of (PNG bytes, width, height, cardWidth, slotCount).</returns>
  public static (byte[] PngBytes, int Width, int Height, int CardWidth, int SlotCount) RenderDeckToPngBytes(
    Deck deck,
    int columns = DefaultColumns,
    double cardHeight = DefaultCardHeight)
  {
    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();

      bool originalInstantRender = EnableInstantRender();
      try
      {
        dynamic deckGrouping = Unbind(deck);
        var result = RenderDeckCore(deckGrouping, columns, cardHeight);
        byte[] pixels = result.Item1;
        int width = result.Item2;
        int height = result.Item3;

        // Calculate card width and slot count
        int cardWidth = (int)(cardHeight * CardAspectRatio);
        int slotCount = Cast<int>(s_cachedVM!.Slots.Count);

        // Convert to PNG bytes
        int stride = width * 4; // BGRA32
        var bitmap = new WriteableBitmap(
          width, height, 96.0, 96.0, PixelFormats.Bgra32, null);

        bitmap.Lock();
        try
        {
          Marshal.Copy(pixels, 0, bitmap.BackBuffer, pixels.Length);
          bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
          bitmap.Unlock();
        }

        using var memStream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(memStream);

        return (memStream.ToArray(), width, height, cardWidth, slotCount);
      }
      finally
      {
        RestoreInstantRender(originalInstantRender);
      }
    }
  }

  /// <summary>
  /// Renders a deck and returns both the PNG image and slot metadata in one call.
  /// This avoids the need to call GetSlotMetadata separately.
  /// </summary>
  /// <param name="deck">The deck to render.</param>
  /// <param name="columns">Number of columns in the grid.</param>
  /// <param name="cardHeight">Height of each card in pixels.</param>
  /// <returns>Tuple of (PNG bytes, width, height, cardWidth, slots).</returns>
  public static (byte[] PngBytes, int Width, int Height, int CardWidth, List<SlotInfo> Slots) RenderDeckWithMetadata(
    Deck deck,
    int columns = DefaultColumns,
    double cardHeight = DefaultCardHeight)
  {
    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();

      bool originalInstantRender = EnableInstantRender();
      try
      {
        dynamic deckGrouping = Unbind(deck);
        var result = RenderDeckCore(deckGrouping, columns, cardHeight);
        byte[] pixels = result.Item1;
        int width = result.Item2;
        int height = result.Item3;

        // Calculate card width
        int cardWidth = (int)(cardHeight * CardAspectRatio);
        
        // Extract slot metadata while VM still has the deck loaded
        int slotCount = Cast<int>(s_cachedVM!.Slots.Count);
        var slots = new List<SlotInfo>(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
          dynamic slot = s_cachedVM.Slots[i];
          dynamic topCard = slot.TopCard;
          dynamic cardDef = topCard.CardDefinition;
          int cardId = Cast<int>(cardDef.Id);
          string name = Cast<string>(cardDef.Name);
          int quantity = Cast<int>(slot.Cards.Count);
          slots.Add(new SlotInfo(i, cardId, name, quantity));
        }

        // Convert to PNG bytes
        int stride = width * 4; // BGRA32
        var bitmap = new WriteableBitmap(
          width, height, 96.0, 96.0, PixelFormats.Bgra32, null);

        bitmap.Lock();
        try
        {
          Marshal.Copy(pixels, 0, bitmap.BackBuffer, pixels.Length);
          bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
          bitmap.Unlock();
        }

        using var memStream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(memStream);

        return (memStream.ToArray(), width, height, cardWidth, slots);
      }
      finally
      {
        RestoreInstantRender(originalInstantRender);
      }
    }
  }

  /// <summary>
  /// Renders a deck as a grid and saves directly to a PNG file.
  /// </summary>
  /// <param name="deck">The deck to render.</param>
  /// <param name="filePath">Output file path.</param>
  /// <param name="columns">Number of columns in the grid.</param>
  /// <param name="cardHeight">Height of each card in pixels.</param>
  public static void SaveDeckAsPng(
    Deck deck,
    string filePath,
    int columns = DefaultColumns,
    double cardHeight = DefaultCardHeight)
  {
    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();

      bool originalInstantRender = EnableInstantRender();
      try
      {
        dynamic deckGrouping = Unbind(deck);
        var result = RenderDeckCore(deckGrouping, columns, cardHeight);
        SaveGridAsPng(result.Item1, result.Item2, result.Item3, filePath);
      }
      finally
      {
        RestoreInstantRender(originalInstantRender);
      }
    }
  }

  /// <summary>
  /// Gets the slot metadata from the GridRenderer's VM after loading a deck.
  /// Returns the exact order and card info that the renderer uses.
  /// </summary>
  /// <param name="deck">The deck to get slot metadata for.</param>
  /// <returns>List of (slotIndex, cardId, name, quantity) tuples.</returns>
  public static List<(int SlotIndex, int CardId, string Name, int Quantity)> GetSlotMetadata(Deck deck)
  {
    var result = new List<(int SlotIndex, int CardId, string Name, int Quantity)>();

    using (DiverCommunicator.BeginUIThreadScope())
    {
      EnsureRenderingInfrastructure();

      dynamic deckGrouping = Unbind(deck);
      s_cachedVM!.SetCardGrouping(deckGrouping, true);

      int slotCount = Cast<int>(s_cachedVM.Slots.Count);
      for (int i = 0; i < slotCount; i++)
      {
        dynamic slot = s_cachedVM.Slots[i];
        // Each slot has a TopCard with CardDefinition, and Cards collection for count
        dynamic topCard = slot.TopCard;
        dynamic cardDef = topCard.CardDefinition;
        int cardId = Cast<int>(cardDef.Id);
        string name = Cast<string>(cardDef.Name);
        // Cards.Count gives us the number of card instances in this slot
        int quantity = Cast<int>(slot.Cards.Count);

        result.Add((i, cardId, name, quantity));
      }
    }

    return result;
  }

  /// <summary>
  /// Clears cached remote objects.
  /// </summary>
  public static void ClearCache()
  {
    s_cachedVM = null;
    s_cachedSelector = null;
    s_cachedPixelFormat = null;
    s_isInitialized = false;
  }
}
