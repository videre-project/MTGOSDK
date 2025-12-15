/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Win32.API;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Graphics;

public static class CardRenderer
{
  private const double ExportWidth = 576.0;
  private const double ExportHeight = 790.0;

  // Cached remote objects for reuse across renders
  private static dynamic? s_cachedTemplate;
  private static dynamic? s_cachedPresenter;
  private static dynamic? s_cachedSize;
  private static dynamic? s_cachedRect;
  private static dynamic? s_cachedPixelFormat;
  private static dynamic? s_cachedRenderTarget;

  /// <summary>
  /// Load the resource dictionary containing card templates.
  /// </summary>
  private static dynamic LoadCardResourceDictionary()
  {
    //
    // The MTGO client already has the card UI resources loaded via
    // Application.Current.Resources (including merged dictionaries).
    //
    // Here we have to ensure it's loaded within the remote process context
    // (e.g. a STA thread with a Dispatcher) so WPF resources are accessible.
    //
    var appType = RemoteClient.GetInstanceType(typeof(Application).FullName!);
    var currentProp = appType.GetProperty("Current");
    if (currentProp is null)
      throw new MissingMemberException("System.Windows.Application.Current was not found in the remote process.");

    dynamic app = currentProp.GetValue(null);
    if (app is null)
      throw new InvalidOperationException("Application.Current is null in the remote process.");

    dynamic resources = app.Resources;
    if (resources is null)
      throw new InvalidOperationException("Application.Current.Resources is null in the remote process.");

    return resources;
  }

  /// <summary>
  /// Initializes or retrieves cached remote objects for rendering.
  /// Must be called within a UI thread scope.
  /// </summary>
  private static void EnsureRenderingInfrastructure()
  {
    if (!RemoteClient.IsInitialized)
    {
      throw new InvalidOperationException("RemoteClient is not initialized.");
    }
    RemoteClient.Disposed += (_, _) => ClearCache();

    if (s_cachedTemplate is null)
    {
      var resourceDict = LoadCardResourceDictionary();
      s_cachedTemplate = resourceDict["baseCardViewExport"];
    }

    if (s_cachedPresenter is null)
    {
      s_cachedPresenter = RemoteClient.CreateInstance<ContentPresenter>();
      s_cachedPresenter.ContentTemplate = s_cachedTemplate;
      s_cachedPresenter.Width = ExportWidth;
      s_cachedPresenter.Height = ExportHeight;
    }

    if (s_cachedSize is null)
    {
      s_cachedSize = RemoteClient.CreateInstance<Size>(ExportWidth, ExportHeight);
    }

    if (s_cachedRect is null)
    {
      s_cachedRect = RemoteClient.CreateInstance<Rect>(0.0, 0.0, ExportWidth, ExportHeight);
    }

    if (s_cachedPixelFormat is null)
    {
      var pixelFormatsType = RemoteClient.GetInstanceType(typeof(PixelFormats).FullName!);
      var pbgra32Prop = pixelFormatsType.GetProperty("Pbgra32");
      s_cachedPixelFormat = pbgra32Prop!.GetValue(null);
    }

    if (s_cachedRenderTarget is null)
    {
      s_cachedRenderTarget = RemoteClient.CreateInstance<RenderTargetBitmap>(
        (int)ExportWidth,
        (int)ExportHeight,
        96.0,
        96.0,
        s_cachedPixelFormat
      );
    }
  }

  /// <summary>
  /// Renders a card and returns the raw pixel data from process memory.
  /// </summary>
  /// <param name="viewModel">The DetailsViewModel for the card.</param>
  /// <returns>Raw BGRA pixel data for the rendered card.</returns>
  /// <remarks>
  /// Assumes caller is already within a UI thread scope.
  /// </remarks>
  private static byte[] RenderCardToPixelsCore(dynamic viewModel)
  {
    // Update the presenter's content (reusing the presenter)
    s_cachedPresenter!.Content = viewModel;

    // Force the visual tree to be created and laid out
    s_cachedPresenter.Measure(s_cachedSize);
    s_cachedPresenter.Arrange(s_cachedRect);
    s_cachedPresenter.UpdateLayout();

    // Clear and re-render using the cached RenderTargetBitmap
    s_cachedRenderTarget!.Clear();
    s_cachedRenderTarget.Render(s_cachedPresenter);

    // Create a WriteableBitmap from the RenderTargetBitmap to copy pixel data
    var writeableBitmap = RemoteClient.CreateInstance<WriteableBitmap>(
      s_cachedRenderTarget
    );

    // Lock the WriteableBitmap to access BackBuffer
    writeableBitmap.Lock();
    try
    {
      // Get the back buffer pointer and stride from the remote WriteableBitmap
      IntPtr backBuffer = Cast<IntPtr>(writeableBitmap.BackBuffer);
      int stride = writeableBitmap.BackBufferStride;
      int bufferSize = stride * (int)ExportHeight;

      // Read the pixel data directly from the remote process memory
      byte[] pixels = new byte[bufferSize];
      bool success = Kernel32.ReadProcessMemory(
        RemoteClient.ClientProcess.Handle,
        backBuffer,
        pixels,
        (nuint)bufferSize,
        out nuint _
      );

      if (!success)
        throw new InvalidOperationException("Failed to read bitmap pixels from remote process memory.");

      return pixels;
    }
    finally
    {
      writeableBitmap.Unlock();
    }
  }

  /// <summary>
  /// Renders a single card. Opens a UI thread scope for each card.
  /// </summary>
  private static byte[] RenderCardToPixels(Card cardDefinition)
  {
    // All WPF operations must be on the same UI thread
    using (DiverCommunicator.BeginUIThreadScope())
    {
      // Ensure all rendering infrastructure is initialized
      EnsureRenderingInfrastructure();

      // Create the ViewModel that the template binds to
      var detailsViewModel = new DetailsViewModel(cardDefinition);

      return RenderCardToPixelsCore(detailsViewModel);
    }
  }

  /// <summary>
  /// Renders cards to raw BGRA pixel data.
  /// </summary>
  /// <param name="cards">The cards to render.</param>
  /// <returns>Raw BGRA pixel data for each card.</returns>
  public static IEnumerable<byte[]> RenderCards(IEnumerable<Card> cards)
  {
    foreach (var card in cards)
    {
      yield return RenderCardToPixels(card);
    }
  }

  /// <summary>
  /// Renders all cards in a single batch within one UI thread scope.
  /// Yields each bitmap as soon as it's rendered.
  /// </summary>
  /// <param name="cards">The cards to render.</param>
  /// <returns>Raw BGRA pixel data for each card, yielded as rendered.</returns>
  public static IEnumerable<byte[]> RenderCardsBatch(IEnumerable<Card> cards)
  {
    // Create all ViewModels in parallel (doesn't need UI thread)
    var viewModels = cards
      .AsParallel()
      .AsOrdered()
      .Select(card => new DetailsViewModel(card))
      .ToList();

    // Render all cards on UI thread (must be sequential)
    using (DiverCommunicator.BeginUIThreadScope())
    {
      // Ensure all rendering infrastructure is initialized
      EnsureRenderingInfrastructure();

      foreach (var viewModel in viewModels)
      {
        yield return RenderCardToPixelsCore(viewModel);
      }
    }
  }

  /// <summary>
  /// Asynchronously renders cards and yields BGRA pixel data as an async stream.
  /// </summary>
  /// <param name="cards">The cards to render.</param>
  /// <param name="cancellationToken">A cancellation token.</param>
  /// <returns>An async stream of raw BGRA pixel data for each card.</returns>
  /// <remarks>
  /// Rendering must occur on a single UI thread. This is intentionally
  /// sequential to avoid threading issues with WPF.
  /// </remarks>
  public static async IAsyncEnumerable<byte[]> RenderCardsAsync(
    IEnumerable<Card> cards,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    foreach (var card in cards)
    {
      cancellationToken.ThrowIfCancellationRequested();
      // Offload the work so callers don't block, but keep rendering sequential.
      yield return await Task.Run(() => RenderCardToPixels(card),
                                  cancellationToken).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Clears the cached rendering infrastructure.
  /// Call this if you need to free remote resources.
  /// </summary>
  public static void ClearCache()
  {
    s_cachedTemplate = null;
    s_cachedPresenter = null;
    s_cachedSize = null;
    s_cachedRect = null;
    s_cachedPixelFormat = null;
    s_cachedRenderTarget = null;
  }

  public static void SaveCardAsPng(byte[] pixelData, string filePath)
  {
    int width = (int)ExportWidth;
    int height = (int)ExportHeight;
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

    using (var fileStream = new FileStream(filePath, FileMode.Create))
    {
      var encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      encoder.Save(fileStream);
    }
  }
}
