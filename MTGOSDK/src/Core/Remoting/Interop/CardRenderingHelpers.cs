/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

#if NETFRAMEWORK
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif


namespace MTGOSDK.Core.Remoting.Interop;

#if NETFRAMEWORK

/// <summary>
/// In-process helper for rendering full card images (including non-card items) to PNG bytes.
/// All MTGO types are resolved at runtime via reflection.
/// </summary>
public static class CardRenderingHelpers
{
  //
  // Reflection type/member cache
  //

  // WotC.MtGO.Client.Model.Core.ResourceManagement.VisualResourceManager
  private static Type   s_vrmType;
  private static object s_vrmInstance;            // singleton
  private static MethodInfo s_getCardResource;    // GetCardResource(ICardDefinition, bool)
  private static MethodInfo s_getStoreResource;   // GetStoreResource(ICardDefinition)

  // WotC.MtGO.Client.Model.ResourceManagement.IVisualResource
  private static PropertyInfo s_viewProp;         // .View  → Uri
  private static PropertyInfo s_loadStateProp;    // .LoadState → enum
  private static MethodInfo   s_loadMethod;       // Load(VisualResourcePriority)
  private static MethodInfo   s_performOnLoad;    // PerformNowOrOnLoadComplete(Action)

  // VisualResourceLoadState enum value for "Loaded"
  private static object s_loadedState;

  // VisualResourcePriority enum value for "RequestedOnScreen" (200)
  private static object s_priorityOnScreen;

  // ICardGrouping.Items → IEnumerable<ICardQuantityPair>
  private static PropertyInfo s_groupingItemsProp;

  // ICardQuantityPair.CardDefinition → ICardDefinition
  private static PropertyInfo s_cardDefProp;

  // ICardDefinition properties we need
  private static PropertyInfo s_isTicketProp;      // IsTicket → bool
  private static PropertyInfo s_isDigitalObjProp;  // IsDigitalObject → bool

  // WotC.MtGO.Client.Model.Core.BitmapUtils.LoadLocalBitmap(string) → BitmapImage
  private static MethodInfo s_loadLocalBitmap;

  // Shiny.Card.Converters.ArtInFrameToBitmapConverter — bitmap LRU cache pre-warming.
  // BitmapUtils.LoadBitmap (called by GetOrCreateBitmapSource) requires STA, so the
  // actual pre-warming call must happen inside RunOnSTA, not in the pre-load loop.
  private static object     s_bitmapConverterInstance; // ArtInFrameToBitmapConverter.Instance
  private static MethodInfo s_getBitmapSourceMethod;   // GetOrCreateBitmapSource(Uri, bool)

  // WotC.MtGO.Client.Model.Core.Collection.CardGrouping + CardQuantityPair — used by
  // RenderCardDefsToFramedGridPng to build an ICardGrouping from ICardDefinition objects
  // directly, bypassing the Deck/ReconcileCards/GetCardDefinitionForCatId path.
  private static Type            s_mtgoCardGroupingType;  // CardGrouping (concrete)
  private static ConstructorInfo s_cqpCtorWithCardDef;    // CardQuantityPair.ctor(ICardDefinition, int, int)
  private static MethodInfo      s_addItemsMethod;        // ICardGrouping.AddItems(IEnumerable<ICardQuantityPair>)

  // ICardSlot.TopCard → DetailsViewModel.CardDefinition → ICardDefinition.Id
  // Used by RenderCardIdsToFramedGridPngWithLayout to read the catalog ID of each
  // rendered slot in display order.
  private static PropertyInfo s_topCardProp;      // ICardSlot.TopCard → DetailsViewModel
  private static PropertyInfo s_slotCardDefProp;  // DetailsViewModel.CardDefinition → ICardDefinition

  // CardDataManager.DigitalObjectsByCatId — used by RenderCardIdsToFramedGridPng to
  // resolve catalog IDs directly without the GetCardDefinitionForCatId fallback logic.
  private static object      s_cardDataManagerInstance;   // ICardDataManager singleton
  private static PropertyInfo s_digitalObjectsByCatIdProp; // Dictionary<int, ICardDefinition>

  private static bool s_initialized = false;
  private static readonly object s_initLock = new object();

  //
  // WPF rendering pipeline
  //

  // Shiny.CardManager.ViewModels.CardGroupingViewModel
  private static object      s_cgvmInstance;     // singleton VM
  private static MethodInfo  s_setCardGrouping;  // SetCardGrouping(ICardGrouping, bool)
  private static PropertyInfo s_slotsProp;       // Slots → IEnumerable

  // Shiny.CardManager.Controls.CardStackItemsSelector
  private static object       s_selectorInstance;  // singleton control
  private static PropertyInfo s_cardHeightProp;    // CardHeight (double)
  private static PropertyInfo s_aspectRatioProp;   // AspectRatio (double)
  private static PropertyInfo s_itemsSourceProp;   // ItemsSource (IEnumerable)

  // Shiny.Card.Converters.ArtInFrameToBrushConverter.InstantRender (static bool)
  private static PropertyInfo s_instantRenderProp;

  private static bool s_wpfInitialized = false;
  private static readonly object s_wpfInitLock = new object();

  //
  // Initialization
  //

  /// <summary>
  /// Resolves and caches all MTGO reflection references.
  /// </summary>
  /// <remarks>
  /// Safe to call multiple times.
  /// </remarks>
  private static void EnsureInitialized()
  {
    if (s_initialized) return;
    lock (s_initLock)
    {
      if (s_initialized) return;

      const BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
      const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

      // ── VisualResourceManager ─────────────────────────────────────────────
      s_vrmType = FindType(
        "WotC.MtGO.Client.Model.Core.ResourceManagement.VisualResourceManager");

      // Singleton instance via static property
      var instanceProp = s_vrmType.GetProperty("Instance", AnyStatic);
      s_vrmInstance = instanceProp!.GetValue(null);

      // GetCardResource(ICardDefinition cardDefinition, bool isAlt = false)
      s_getCardResource = s_vrmType.GetMethod("GetCardResource",
        AnyInstance,
        binder: null,
        types: new[] { FindType("WotC.MtGO.Client.Model.ICardDefinition"), typeof(bool) },
        modifiers: null);

      // GetStoreResource(ICardDefinition cardDefinition)
      s_getStoreResource = s_vrmType.GetMethod("GetStoreResource",
        AnyInstance,
        binder: null,
        types: new[] { FindType("WotC.MtGO.Client.Model.ICardDefinition") },
        modifiers: null);

      // ── IVisualResource ───────────────────────────────────────────────────
      var ivr = FindType(
        "WotC.MtGO.Client.Model.ResourceManagement.IVisualResource");

      s_viewProp      = ivr.GetProperty("View",      AnyInstance);
      s_loadStateProp = ivr.GetProperty("LoadState", AnyInstance);
      s_loadMethod    = ivr.GetMethod  ("Load",      AnyInstance);
      // One-arg overload on IVisualResource itself (confirmed in interface source)
      s_performOnLoad = ivr.GetMethod  ("PerformNowOrOnLoadComplete",
        AnyInstance,
        binder: null,
        types: new[] { typeof(Action) },
        modifiers: null);

      // Resolve enum values by their integer backing value
      var loadStateType = FindType(
        "WotC.MtGO.Client.Model.ResourceManagement.VisualResourceLoadState");
      s_loadedState = Enum.ToObject(loadStateType, 3); // Loaded = 3

      var priorityType = FindType(
        "WotC.MtGO.Client.Model.ResourceManagement.VisualResourcePriority");
      s_priorityOnScreen = Enum.ToObject(priorityType, 200); // RequestedOnScreen = 200

      // ── ICardQuantityPair / ICardDefinition helpers ───────────────────────
      var iCardGroupingType2 = FindType("WotC.MtGO.Client.Model.Collection.ICardGrouping");
      s_groupingItemsProp = iCardGroupingType2.GetProperty("Items", AnyInstance);

      var cqp = FindType("WotC.MtGO.Client.Model.ICardQuantityPair");
      s_cardDefProp = cqp.GetProperty("CardDefinition", AnyInstance);

      // IsTicket and IsDigitalObject are defined on the MagicEntityDefinition
      // abstract base class, which all concrete card definitions inherit from.
      var med = FindType(
        "WotC.MtGO.Client.Model.Core.MagicEntityDefinition");
      s_isTicketProp     = med.GetProperty("IsTicket",      AnyInstance);
      s_isDigitalObjProp = med.GetProperty("IsDigitalObject", AnyInstance);

      // ── BitmapUtils ───────────────────────────────────────────────────────
      var bitmapUtils = FindType(
        "WotC.MtGO.Client.Model.Core.BitmapUtils");
      s_loadLocalBitmap = bitmapUtils.GetMethod(
        "LoadLocalBitmap",
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(string) },
        modifiers: null);

      // ── ArtInFrameToBitmapConverter ───────────────────────────────────────
      // Used to pre-warm the bitmap LRU cache (max 200 entries) before rendering.
      // Cards not viewed recently in MTGO may have been evicted; without pre-warming,
      // GetOrCreateBitmapSource would call BitmapUtils.LoadBitmap synchronously during
      // the WPF layout pass. Pre-warming here ensures it's in cache before layout runs.
      var artBitmapConverterType = FindType(
        "Shiny.Card.Converters.ArtInFrameToBitmapConverter");
      s_bitmapConverterInstance = artBitmapConverterType
        .GetProperty("Instance", AnyStatic)?.GetValue(null);
      s_getBitmapSourceMethod = artBitmapConverterType.GetMethod(
        "GetOrCreateBitmapSource", AnyInstance, null,
        new[] { typeof(Uri), typeof(bool) }, null);

      // ── CardGrouping + CardQuantityPair (for RenderCardDefsToFramedGridPng) ──
      // We need to build an ICardGrouping from ICardDefinition objects directly,
      // without going through the Deck/ReconcileCards/GetCardDefinitionForCatId
      // path which can silently replace unknown IDs with fallback IDs.
      s_mtgoCardGroupingType = FindType(
        "WotC.MtGO.Client.Model.Core.Collection.CardGrouping");

      // CardQuantityPair(ICardDefinition cardDefinition, int quantity, int permission,
      //                  AttributeAnnotation annotation = NotSet)
      var iCardDefType = FindType("WotC.MtGO.Client.Model.ICardDefinition");
      var cqpType = FindType("WotC.MtGO.Client.Model.Core.CardQuantityPair");
      var annotationType = FindType("WotC.MtGO.Client.Model.Collection.AttributeAnnotation");
      // Find the 4-parameter constructor
      s_cqpCtorWithCardDef = cqpType.GetConstructors(AnyInstance)
        .FirstOrDefault(c =>
        {
          var ps = c.GetParameters();
          return ps.Length >= 3
              && ps[0].ParameterType.IsAssignableFrom(iCardDefType)
              && ps[1].ParameterType == typeof(int)
              && ps[2].ParameterType == typeof(int);
        });

      // AddItems(IEnumerable<ICardQuantityPair>, ulong? operationId)
      var iCqpType = FindType("WotC.MtGO.Client.Model.ICardQuantityPair");
      var iEnumCqpType = typeof(System.Collections.Generic.IEnumerable<>)
        .MakeGenericType(iCqpType);
      s_addItemsMethod = s_mtgoCardGroupingType.GetMethod(
        "AddItems", AnyInstance, null,
        new[] { iEnumCqpType, typeof(ulong?) }, null);
      // Some overloads may not match exactly; fall back to name search
      if (s_addItemsMethod == null)
        s_addItemsMethod = s_mtgoCardGroupingType.GetMethod("AddItems", AnyInstance);

      // ICardSlot.TopCard → DetailsViewModel.CardDefinition → ICardDefinition.Id
      // Used to recover the catalog ID from each slot in display order.
      var cardSlotType = FindType("Shiny.CardManager.Interfaces.ICardSlot");
      if (cardSlotType != null)
        s_topCardProp = cardSlotType.GetProperty("TopCard", AnyInstance);
      var detailsVmType = FindType("Shiny.CardManager.ViewModels.DetailsViewModel");
      if (detailsVmType != null)
        s_slotCardDefProp = detailsVmType.GetProperty("CardDefinition", AnyInstance);

      // CardDataManager via ObjectProvider.Get<ICardDataManager>() — the same
      // service-locator call used by CollectionManager and CardGroupingViewModel.
      // CDM is DI-injected (no static Instance), so ObjectProvider is the only
      // reliable way to obtain it inside the MTGO process.
      try
      {
        var icdmType = FindType("WotC.MtGO.Client.Model.Core.ICardDataManager");
        var objProvType = FindType(
          "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider");
        var getMethod = objProvType
          .GetMethods(BindingFlags.Public | BindingFlags.Static)
          .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition);
        if (getMethod != null)
        {
          s_cardDataManagerInstance = getMethod
            .MakeGenericMethod(icdmType)
            .Invoke(null, null);
        }
        if (s_cardDataManagerInstance != null)
        {
          s_digitalObjectsByCatIdProp = s_cardDataManagerInstance.GetType()
            .GetProperty("DigitalObjectsByCatId",
              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
      }
      catch { /* CDM will be re-tried lazily on first RenderCardIdsToFramedGridPng call */ }

      s_initialized = true;
    }
  }

  /// <summary>
  /// Walks all loaded assemblies looking for a type by full name.
  /// </summary>
  private static Type FindType(string fullName)
  {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      var t = asm.GetType(fullName, throwOnError: false);
      if (t != null) return t;
    }
    throw new TypeLoadException(
      $"[CardRenderingHelpers] Could not locate type '{fullName}' in any loaded assembly.");
  }

  // WPF infrastructure

  /// <summary>
  /// Creates and caches the CardGroupingViewModel and CardStackItemsSelector instances used by the WPF rendering pipeline.
  /// Must be called from within a RunOnSTA block.
  /// </summary>
  private static void EnsureWpfInfrastructure()
  {
    if (s_wpfInitialized) return;
    lock (s_wpfInitLock)
    {
      if (s_wpfInitialized) return;

      const BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
      const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

      // ── ArtInFrameToBrushConverter.InstantRender ──────────────────────────
      var converterType = FindType("Shiny.Card.Converters.ArtInFrameToBrushConverter");
      s_instantRenderProp = converterType.GetProperty("InstantRender", AnyStatic);

      // ── CardGroupingViewModel ─────────────────────────────────────────────
      var cgvmType = FindType("Shiny.CardManager.ViewModels.CardGroupingViewModel");
      s_cgvmInstance = Activator.CreateInstance(cgvmType);

      // Disable the card-legality filter so non-card items (Event Tickets,
      // boosters, All Access Tokens, etc.) are not rejected by ItemIsLegalForDeck
      // (which returns `card.IsCard && !card.IsTrophy`, i.e. false for all non-cards).
      var legalityField = cgvmType.GetField(
        "m_enableCardLegalityCheck", AnyInstance);
      legalityField?.SetValue(s_cgvmInstance, false);

      var iCardGroupingType = FindType("WotC.MtGO.Client.Model.Collection.ICardGrouping");
      s_setCardGrouping = cgvmType.GetMethod(
        "SetCardGrouping", AnyInstance, null,
        new[] { iCardGroupingType, typeof(bool) }, null);

      s_slotsProp = cgvmType.GetProperty("Slots", AnyInstance);

      // Layout: Stack = one slot per card, no piling
      var layoutType  = FindType("Shiny.CardManager.CardViewLayout");
      var stackLayout = Enum.Parse(layoutType, "Stack");
      cgvmType.GetMethod("SetLayout", AnyInstance)
              ?.Invoke(s_cgvmInstance, new[] { stackLayout, (object)false });

      // Sort: pile:none + Name,CardId for deterministic, unpiled ordering.
      //
      // IMPORTANT: CardSortMode.Initialize() sets m_sortingCriteria to the
      // shared CardSortingPresets.SortByCMC list instance.  The SortingCriteria
      // property setter requires a List<CardSortKey> cast; the setter also
      // checks IsReadOnly.  To avoid mutating the shared preset list we:
      //  a) create a fresh List<CardSortKey> via reflection, and
      //  b) write it directly to the backing field m_sortingCriteria.
      var sortModeType = FindType("Shiny.CardManager.CardSortMode");
      var sortKeyType  = FindType("Shiny.CardManager.CardSortKey");
      var sortMode     = Activator.CreateInstance(sortModeType, "pile:none;sort:Name,CardId");

      var noneKey   = sortKeyType.GetField("None",   AnyStatic)?.GetValue(null);
      var nameKey   = sortKeyType.GetField("Name",   AnyStatic)?.GetValue(null);
      var cardIdKey = sortKeyType.GetField("CardId", AnyStatic)?.GetValue(null);

      sortModeType.GetProperty("PilingCriterion", AnyInstance)?.SetValue(sortMode, noneKey);

      // Create a fresh List<CardSortKey> so we never touch the shared SortByCMC.
      var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(sortKeyType);
      var freshCriteria = Activator.CreateInstance(listType);
      var addM = listType.GetMethod("Add");
      addM?.Invoke(freshCriteria, new[] { nameKey });
      addM?.Invoke(freshCriteria, new[] { cardIdKey });

      // Write directly to the backing field to avoid IsReadOnly check and cast validation.
      var criteriaField = sortModeType.GetField("m_sortingCriteria", AnyInstance);
      if (criteriaField != null)
        criteriaField.SetValue(sortMode, freshCriteria);
      else
      {
        // Fallback: use property setter (works when IsReadOnly = false, which it is
        // for a freshly constructed CardSortMode).
        sortModeType.GetProperty("SortingCriteria", AnyInstance)?.SetValue(sortMode, freshCriteria);
      }

      cgvmType.GetMethod("SetSortMode", AnyInstance)
              ?.Invoke(s_cgvmInstance, new[] { sortMode, (object)false, (object)false });

      // ── CardStackItemsSelector ────────────────────────────────────────────
      var selectorType = FindType("Shiny.CardManager.Controls.CardStackItemsSelector");
      s_selectorInstance = Activator.CreateInstance(selectorType);
      s_cardHeightProp   = selectorType.GetProperty("CardHeight",   AnyInstance);
      s_aspectRatioProp  = selectorType.GetProperty("AspectRatio",  AnyInstance);
      // CardStackItemsSelector does not inherit ItemsControl, so we must look up
      // ItemsSource by reflection rather than casting.
      s_itemsSourceProp  = selectorType.GetProperty("ItemsSource",  AnyInstance);

      // Enable layout rounding so all element positions snap to device pixels.
      // This is a safety net in case any child control template still produces
      // sub-pixel positions despite us using integer card dimensions.
      ((System.Windows.FrameworkElement)s_selectorInstance).UseLayoutRounding = true;

      // Hide scrollbars so they don't affect rendered dimensions
      var selectorElem = (System.Windows.UIElement)s_selectorInstance;
      System.Windows.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
        selectorElem, System.Windows.Controls.ScrollBarVisibility.Hidden);
      System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(
        selectorElem, System.Windows.Controls.ScrollBarVisibility.Hidden);

      // Initialize the visual tree so the first render is fully realized
      ((System.Windows.FrameworkElement)s_selectorInstance).ApplyTemplate();

      s_wpfInitialized = true;
    }
  }

  // Resource resolution helpers

  /// <summary>
  /// Gets the correct IVisualResource for a card definition.
  /// </summary>
  private static object GetVisualResource(object cardDef)
  {
    // IS_TICKET: must go through GetStoreResource to get the Store-folder URI
    bool isTicket = s_isTicketProp != null && (bool)s_isTicketProp.GetValue(cardDef);
    if (isTicket)
      return s_getStoreResource.Invoke(s_vrmInstance, new[] { cardDef });

    // Everything else (IS_DIGITALOBJECT, regular cards) works via GetCardResource
    return s_getCardResource.Invoke(s_vrmInstance, new object[] { cardDef, false });
  }

  /// <summary>
  /// Blocks until resource reaches Loaded state, or until timeoutMs elapses.
  /// </summary>
  private static bool WaitForLoad(object resource, int timeoutMs = 8000)
  {
    // Fast-path: already loaded
    var currentState = s_loadStateProp.GetValue(resource);
    if (currentState.Equals(s_loadedState)) return true;

    using var ready = new ManualResetEventSlim(false);

    // Register the completion callback before calling Load, so we never miss
    // the transition that happens on a background thread.
    Action callback = () => ready.Set();
    s_performOnLoad.Invoke(resource, new object[] { callback });

    // Kick off the download (enqueues if not already in flight).
    s_loadMethod.Invoke(resource, new[] { s_priorityOnScreen });

    return ready.Wait(timeoutMs);
  }

  // Public API

  /// <summary>
  /// Loads the art for a single card/item definition and returns the raw BGRA pixel bytes and dimensions.
  /// Returns null if the resource could not be loaded.
  /// </summary>
  public static CardArtPixels? LoadCardArtPixels(object cardDef, int timeoutMs = 8000)
  {
    EnsureInitialized();

    // 1. Resolve the IVisualResource for this definition
    object resource = GetVisualResource(cardDef);
    if (resource == null) return null;

    // 2. Block until the file is on disk
    bool loaded = WaitForLoad(resource, timeoutMs);
    if (!loaded) return null;

    // 3. Get the local file URI
    Uri view = (Uri)s_viewProp.GetValue(resource);
    if (view == null || view.OriginalString == ".") return null;

    // 4. Decode the bitmap synchronously.
    //    BitmapUtils.LoadLocalBitmap opens a FileStream and uses
    //    BitmapCacheOption.OnLoad so the file handle is released immediately.
    //    All WPF imaging must run on an STA thread.
    BitmapSource bmp = null;
    RunOnSTA(() =>
    {
      if (view.IsFile && s_loadLocalBitmap != null)
      {
        bmp = (BitmapSource)s_loadLocalBitmap.Invoke(null, new object[] { view.LocalPath });
      }
      else
      {
        // Fallback for non-file URIs (rare; CDN path still in View)
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.UriSource = view;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.EndInit();
        bi.Freeze();
        bmp = bi;
      }
    });

    if (bmp == null) return null;

    // 5. Convert to Bgra32 so the caller gets a well-known pixel format.
    //    ConvertedBitmap is the idiomatic WPF way to do this in-process.
    BitmapSource bgra = null;
    RunOnSTA(() =>
    {
      if (bmp.Format == PixelFormats.Bgra32)
      {
        bgra = bmp;
      }
      else
      {
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = bmp;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();
        bgra = converted;
      }
    });

    if (bgra == null) return null;

    int w = bgra.PixelWidth;
    int h = bgra.PixelHeight;
    int stride = w * 4;
    byte[] pixels = new byte[stride * h];
    bgra.CopyPixels(pixels, stride, 0);

    return new CardArtPixels(pixels, w, h);
  }

  /// <summary>
  /// Renders a list of card definitions into a grid of card art and returns PNG bytes.
  /// </summary>
  public static byte[] RenderCardDefsToGridPng(
    IEnumerable cardDefs,
    int columns = 5,
    int cardHeight = 300,
    int timeoutMsPerCard = 8000)
  {
    EnsureInitialized();

    const double AspectRatio = 5.0 / 7.0; // MTGO card default
    int cardWidth = (int)Math.Ceiling(cardHeight * AspectRatio);

    // Collect all definitions into a list for dimension calculation
    var defList = new System.Collections.Generic.List<object>();
    foreach (var def in cardDefs)
      defList.Add(def);

    int total = defList.Count;
    if (total == 0)
      return Array.Empty<byte>();

    int rows = (total + columns - 1) / columns;
    int gridW = columns * cardWidth;
    int gridH = rows * cardHeight;

    // Allocate a flat Bgra32 buffer for the whole grid
    byte[] gridPixels = new byte[gridW * gridH * 4];

    for (int i = 0; i < total; i++)
    {
      int col = i % columns;
      int row = i / columns;
      int cellX = col * cardWidth;
      int cellY = row * cardHeight;

      CardArtPixels? art = LoadCardArtPixels(defList[i], timeoutMsPerCard);
      if (art == null) continue;

      // Scale the decoded art into the cell using nearest-neighbour.
      // We keep it simple here because WPF's WriteableBitmap/TransformedBitmap
      // must run on an STA thread and would complicate the loop.
      // For production use, replace with a higher-quality resampler.
      BlitScaled(art.Value, cardWidth, cardHeight, gridPixels, gridW, cellX, cellY);
    }

    // Encode the Bgra32 buffer as PNG
    byte[] png = null;
    RunOnSTA(() =>
    {
      var wb = new WriteableBitmap(gridW, gridH, 96, 96, PixelFormats.Bgra32, null);
      wb.Lock();
      try
      {
        Marshal.Copy(gridPixels, 0, wb.BackBuffer, gridPixels.Length);
        wb.AddDirtyRect(new Int32Rect(0, 0, gridW, gridH));
      }
      finally
      {
        wb.Unlock();
      }

      using var ms = new MemoryStream();
      var enc = new PngBitmapEncoder();
      enc.Frames.Add(BitmapFrame.Create(wb));
      enc.Save(ms);
      png = ms.ToArray();
    });

    return png ?? Array.Empty<byte>();
  }

  /// <summary>
  /// Renders a set of cards (specified by catalog IDs) as a framed grid PNG and returns slot order and PNG bytes.
  /// </summary>
  public static string RenderCardIdsToFramedGridPngWithLayout(
    string catalogIdsCsv,
    int columns = 5,
    int cardHeight = 300,
    int timeoutMsPerCard = 8000)
  {
    // Re-use the render method; capture slot order via the CGVM Slots after the fact.
    // Both calls share the same s_cgvmInstance so the Slots state is still valid.
    byte[] png = RenderCardIdsToFramedGridPng(
      catalogIdsCsv, columns, cardHeight, timeoutMsPerCard);

    // Read the slot order from the CGVM (Slots was populated by RenderCardIdsToFramedGridPng).
    var slotIds = new System.Collections.Generic.List<int>();
    try
    {
      var slots = s_slotsProp?.GetValue(s_cgvmInstance) as IEnumerable;
      if (slots != null && s_topCardProp != null && s_slotCardDefProp != null)
      {
        foreach (var slot in slots)
        {
          try
          {
            var topCard = s_topCardProp.GetValue(slot);
            var cardDef = topCard != null ? s_slotCardDefProp.GetValue(topCard) : null;
            var idProp  = cardDef?.GetType().GetProperty("Id",
              BindingFlags.Public | BindingFlags.Instance);
            slotIds.Add(idProp != null ? (int)idProp.GetValue(cardDef) : 0);
          }
          catch { slotIds.Add(0); }
        }
      }
    }
    catch { }

    string slotCsv = string.Join(",", slotIds);
    string base64  = Convert.ToBase64String(png);
    return slotCsv + "|" + base64;
  }

  /// <summary>
  /// Renders a list of card definitions as a grid of fully-framed card images using MTGO's WPF pipeline.
  /// </summary>
  public static byte[] RenderCardDefsToFramedGridPng(
    IEnumerable cardDefs,
    int columns = 5,
    int cardHeight = 300,
    int timeoutMsPerCard = 8000)
  {
    EnsureInitialized();

    // Build a fresh CardGrouping from the resolved ICardDefinition objects.
    // We use the CardQuantityPair(ICardDefinition, quantity, permission) constructor
    // so that the card definition is used as-is, without any ID re-resolution.
    object grouping = Activator.CreateInstance(s_mtgoCardGroupingType);
    if (grouping == null)
      return Array.Empty<byte>();

    // Collect ICardQuantityPair wrappers; also accumulate defs for pre-loading.
    var defList = new List<object>();
    foreach (var def in cardDefs)
    {
      if (def == null) continue;
      defList.Add(def);

      if (s_cqpCtorWithCardDef != null && s_addItemsMethod != null)
      {
        try
        {
          // CardQuantityPair(ICardDefinition cardDefinition, int quantity, int permission)
          // permission 215 = the standard deck permission code used by the SDK.
          var annotationType = FindType("WotC.MtGO.Client.Model.Collection.AttributeAnnotation");
          object defaultAnnotation = Enum.ToObject(annotationType, 0); // NotSet
          var pair = s_cqpCtorWithCardDef.Invoke(
            new object[] { def, 1, 215, defaultAnnotation });

          // AddItems(IEnumerable<ICardQuantityPair>, ulong? = null)
          // Wrap in a one-element array that implements IEnumerable<ICardQuantityPair>.
          var iCqpType = FindType("WotC.MtGO.Client.Model.ICardQuantityPair");
          var arrType  = iCqpType.MakeArrayType();
          var arr      = Array.CreateInstance(iCqpType, 1);
          arr.SetValue(pair, 0);
          s_addItemsMethod.Invoke(grouping, new object[] { arr, (ulong?)null });
        }
        catch { /* skip cards that cannot be added */ }
      }
    }

    if (defList.Count == 0)
      return Array.Empty<byte>();

    // Delegate to the main WPF pipeline now that we have a valid ICardGrouping.
    return RenderCardGroupingToGridPng(grouping, columns, cardHeight, timeoutMsPerCard);
  }

  /// <summary>
  /// Renders a set of cards (specified by catalog IDs) as a framed grid PNG.
  /// </summary>
  public static byte[] RenderCardIdsToFramedGridPng(
    string catalogIdsCsv,
    int columns = 5,
    int cardHeight = 300,
    int timeoutMsPerCard = 8000)
  {
    if (string.IsNullOrWhiteSpace(catalogIdsCsv))
      return Array.Empty<byte>();

    var ids = catalogIdsCsv
      .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
      .Select(s => int.TryParse(s.Trim(), out var n) ? (int?)n : null)
      .Where(n => n.HasValue)
      .Select(n => n.Value)
      .ToArray();

    if (ids.Length == 0)
      return Array.Empty<byte>();

    EnsureInitialized();

    // Resolve DigitalObjectsByCatId lazily (CDM may not have been ready at EnsureInitialized time)
    object dict = null;
    MethodInfo tryGetValue = null;
    if (s_digitalObjectsByCatIdProp != null && s_cardDataManagerInstance != null)
    {
      dict = s_digitalObjectsByCatIdProp.GetValue(s_cardDataManagerInstance);
      if (dict != null)
        tryGetValue = dict.GetType().GetMethod("TryGetValue");
    }

    // If we still don't have the dictionary, try ObjectProvider now
    if (dict == null)
    {
      try
      {
        var icdmType = FindType("WotC.MtGO.Client.Model.Core.ICardDataManager");
        var objProvType = FindType(
          "WotC.MtGO.Client.Common.ServiceLocation.ObjectProvider");
        var getMethod = objProvType
          .GetMethods(BindingFlags.Public | BindingFlags.Static)
          .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition);
        if (getMethod != null)
        {
          s_cardDataManagerInstance = getMethod
            .MakeGenericMethod(icdmType)
            .Invoke(null, null);
        }
        if (s_cardDataManagerInstance != null)
        {
          s_digitalObjectsByCatIdProp = s_cardDataManagerInstance.GetType()
            .GetProperty("DigitalObjectsByCatId",
              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
          dict = s_digitalObjectsByCatIdProp?.GetValue(s_cardDataManagerInstance);
          if (dict != null)
            tryGetValue = dict.GetType().GetMethod("TryGetValue");
        }
      }
      catch { }
    }

    var annotationType    = FindType("WotC.MtGO.Client.Model.Collection.AttributeAnnotation");
    object defAnnotation  = Enum.ToObject(annotationType, 0);
    var iCqpType          = FindType("WotC.MtGO.Client.Model.ICardQuantityPair");

    object grouping = Activator.CreateInstance(s_mtgoCardGroupingType);
    if (grouping == null)
      return Array.Empty<byte>();

    int added = 0;
    foreach (int catId in ids)
    {
      object cardDef = null;
      if (dict != null && tryGetValue != null)
      {
        var args = new object[] { catId, null };
        bool found = (bool)(tryGetValue.Invoke(dict, args) ?? false);
        if (found) cardDef = args[1];
      }
      if (cardDef == null) continue;

      try
      {
        if (s_cqpCtorWithCardDef == null || s_addItemsMethod == null) continue;
        var pair = s_cqpCtorWithCardDef.Invoke(new object[] { cardDef, 1, 215, defAnnotation });
        var arr  = Array.CreateInstance(iCqpType, 1);
        arr.SetValue(pair, 0);
        s_addItemsMethod.Invoke(grouping, new object[] { arr, (ulong?)null });
        added++;
      }
      catch { /* skip cards that cannot be wrapped */ }
    }

    if (added == 0)
      return Array.Empty<byte>();

    return RenderCardGroupingToGridPng(grouping, columns, cardHeight, timeoutMsPerCard);
  }

  /// <summary>
  /// Renders an ICardGrouping as a grid of fully-framed card images and returns PNG bytes.
  /// </summary>
  public static byte[] RenderCardGroupingToGridPng(
    object grouping,
    int columns = 5,
    int cardHeight = 300,
    int timeoutMsPerCard = 8000)
  {
    EnsureInitialized();

    // ── Pre-load visual resources ─────────────────────────────────────────────
    // ArtInFrameImageName returns ArtInFrameResource.View, which is "." until
    // the art file has been downloaded.  BaseCardViewModel.Initialize sets
    // ArtInFrameResource and calls Load(RequestedOffScreen) — a low-priority
    // async request.  By pre-loading each resource here (at higher priority and
    // blocking until done), View already returns the real file path when
    // SetCardGrouping/InitializeCardsCache creates the BaseCardViewModels.
    // The WPF binding then reads the correct URI from the start, so
    // ArtInFrameToBrushConverter.Convert (with InstantRender=true) can load
    // the bitmap synchronously before Render() is called.
    // Collect URIs of successfully pre-loaded resources so the STA thread can warm
    // the ArtInFrameToBitmapConverter bitmap cache before the WPF layout runs.
    // (BitmapImage creation requires STA; we only block non-STA here for file I/O.)
    //
    // Fan-out / fan-in: register all completion callbacks and enqueue all loads
    // *before* blocking on any of them.  The resource manager's internal thread
    // pool can then process all art downloads / disk reads concurrently, so the
    // total wait is max(individual loads) rather than sum(individual loads).
    var preloadedUris = new System.Collections.Generic.List<Uri>();
    if (s_groupingItemsProp != null)
    {
      var items = (IEnumerable)s_groupingItemsProp.GetValue(grouping);
      if (items != null)
      {
        var pending = new System.Collections.Generic.List<(object resource, ManualResetEventSlim ready)>();

        // Pass 1 — resolve resources and register callbacks (no blocking).
        foreach (var pair in items)
        {
          try
          {
            var cardDef = s_cardDefProp?.GetValue(pair);
            if (cardDef == null) continue;
            var resource = GetVisualResource(cardDef);
            if (resource == null) continue;

            // Fast path: already in Loaded state — add URI immediately.
            if (s_loadStateProp.GetValue(resource).Equals(s_loadedState))
            {
              var uri = s_viewProp.GetValue(resource) as Uri;
              if (uri != null && uri.OriginalString != ".") preloadedUris.Add(uri);
              continue;
            }

            // Register the completion callback before calling Load so the
            // transition cannot be missed on a background thread.
            var ready = new ManualResetEventSlim(false);
            Action callback = () => ready.Set();
            s_performOnLoad.Invoke(resource, new object[] { callback });
            pending.Add((resource, ready));
          }
          catch { /* skip unresolvable resources */ }
        }

        // Pass 2 — kick off all loads simultaneously.
        foreach (var (resource, _) in pending)
        {
          try { s_loadMethod.Invoke(resource, new[] { s_priorityOnScreen }); }
          catch { }
        }

        // Pass 3 — fan-in: wait for every load with one shared deadline.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsPerCard);
        foreach (var (resource, ready) in pending)
        {
          try
          {
            int ms = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
            ready.Wait(ms);
            var uri = s_viewProp.GetValue(resource) as Uri;
            if (uri != null && uri.OriginalString != ".") preloadedUris.Add(uri);
          }
          catch { }
          finally { ready.Dispose(); }
        }
      }
    }

    const double AspectRatio = 5.0 / 7.0; // MTGO card aspect ratio
    // Use Ceiling (not truncation) so gridW is wide enough that the panel's own
    // internal column calc — floor(gridW / (cardHeight * AspectRatio)) — yields
    // exactly `columns`, not columns-1.
    int cardWidth = (int)Math.Ceiling(cardHeight * AspectRatio);

    byte[] png = null;
    RunOnSTA(() =>
    {
      // Ensure the WPF objects exist (must be on STA/dispatcher thread)
      EnsureWpfInfrastructure();

      // Warm ArtInFrameToBitmapConverter's bitmap LRU cache synchronously on the STA
      // thread. BitmapUtils.LoadBitmap (called inside GetOrCreateBitmapSource) creates
      // WPF BitmapImage objects which require STA. Doing this before SetCardGrouping
      // ensures every art bitmap is in cache by the time the WPF binding evaluates.
      //
      // IMPORTANT: iterate in REVERSE order so the first card slot (which is rendered
      // first) is the LAST warmed, making it the "hottest" (most recently inserted)
      // entry in the 200-entry LRU cache. This prevents the first card's bitmap from
      // being evicted before the WPF binding evaluation reaches it.
      if (s_getBitmapSourceMethod != null && s_bitmapConverterInstance != null)
      {
        for (int i = preloadedUris.Count - 1; i >= 0; i--)
        {
          try
          {
            s_getBitmapSourceMethod.Invoke(
              s_bitmapConverterInstance, new object[] { preloadedUris[i], false });
          }
          catch { }
        }
      }

      bool prevInstantRender = false;
      try
      {
        // Mirror GridRenderer.EnableInstantRender(): force synchronous art load
        // so cards aren't blank when we call renderTarget.Render().
        if (s_instantRenderProp != null)
        {
          prevInstantRender = (bool)s_instantRenderProp.GetValue(null);
          s_instantRenderProp.SetValue(null, true);
        }

        // Configure card dimensions on the selector.
        //
        // IMPORTANT: CardStackItemsControl.OnCardDimensionsChanged computes
        // m_cardSize.Width = CardHeight * AspectRatio as a raw double, e.g.
        // 300 * 0.714285... = 214.285... — a fractional pixel width.  The panel
        // then places card N at col * 214.285... DIPs, which is a sub-pixel
        // position when rendered at 96 DPI (1 DIP = 1 px).  Our crop reads from
        // col * ceil(cardHeight * 5/7) = col * 215, so the origins diverge by
        // col * 0.714... pixels — causing the next card to bleed into the right
        // edge of each slot.
        //
        // Fix: override AspectRatio to the exact ratio cardWidth/cardHeight so
        // m_cardSize.Width = cardHeight * (cardWidth/cardHeight) = cardWidth
        // (exact integer), and cards are arranged at col * cardWidth DIP = pixel.
        s_cardHeightProp?.SetValue(s_selectorInstance, (double)cardHeight);
        s_aspectRatioProp?.SetValue(s_selectorInstance, cardWidth / (double)cardHeight);

// Populate Slots from the grouping.
            // SetCardGrouping(pileCards:true) triggers PileCards(), which runs
            // on a thread-pool task and dispatches its result back to the MTGO
            // UI thread via DispatcherWrapper.BeginInvoke(Normal priority).
            // If we are already ON the MTGO dispatcher we must pump it with a
            // nested DispatcherFrame so that Normal-priority callback can run
            // before we read Slots (otherwise Slots is still null → NRE).
            s_setCardGrouping.Invoke(s_cgvmInstance, new[] { grouping, (object)true });

            IEnumerable slots;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
              // Pump the MTGO dispatcher in a nested frame.  We queue the
              // frame-exit at Background priority (4); the incoming
              // PileCardsCompleted BeginInvoke uses Normal priority (9) which
              // is higher, so it always runs before our exit callback.
              var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMsPerCard);
              while ((slots = (IEnumerable)s_slotsProp.GetValue(s_cgvmInstance)) == null
                     && DateTime.UtcNow < deadline)
              {
                var frame = new System.Windows.Threading.DispatcherFrame(
                  exitWhenRequested: true);
                dispatcher.BeginInvoke(
                  new Action(() => frame.Continue = false),
                  System.Windows.Threading.DispatcherPriority.Background);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
              }
              slots = (IEnumerable)s_slotsProp.GetValue(s_cgvmInstance);
            }
            else
            {
              // Running on a fresh STA thread: the MTGO dispatcher can process
              // PileCardsCompleted on its own thread while we block here.
              using var ready = new ManualResetEventSlim(false);
              var vmNotify = s_cgvmInstance as System.ComponentModel.INotifyPropertyChanged;
              System.ComponentModel.PropertyChangedEventHandler onChange = null;
              onChange = (_, e) =>
              {
                if (e.PropertyName == "Slots" &&
                    s_slotsProp.GetValue(s_cgvmInstance) != null)
                  ready.Set();
              };
              if (vmNotify != null) vmNotify.PropertyChanged += onChange;
              try   { ready.Wait(timeoutMsPerCard); }
              finally { if (vmNotify != null) vmNotify.PropertyChanged -= onChange; }
              slots = (IEnumerable)s_slotsProp.GetValue(s_cgvmInstance);
            }

            if (slots == null)
            {
              // Timed out waiting for PileCards to complete
              png = Array.Empty<byte>();
              return;
            }

            // Bind the selector to the VM's slot collection.
            // CardStackItemsSelector does not inherit ItemsControl, so set via reflection.
        s_itemsSourceProp?.SetValue(s_selectorInstance, slots);

        // Count slots to determine grid dimensions
        int slotCount = 0;
        foreach (var _ in slots) slotCount++;

        int rows    = (slotCount + columns - 1) / columns;
        double gridW = columns * cardWidth;
        double gridH = rows    * cardHeight;

        // BaseTilingItemsPanel is a virtualizing panel: it only creates child
        // CardStackControl elements once it knows its own ActualWidth/Height.
        // The sequence that makes this work off-screen:
        //
        //  1. Measure(gridW, gridH)  - establishes available size in the tree.
        //  2. Arrange(rect)          - fires SizeChangedEvent synchronously on
        //                             the panel; OnSizeChanged → UpdateScrollInfo
        //                             → AddOrRemoveChildren adds N children with
        //                             Width/Height set; DataContext is bound via
        //                             UpdateCardBindings (art loaded sync because
        //                             InstantRender = true).
        //  3. UpdateLayout()         - second layout pass: measures and arranges
        //                             the newly added children; applies their
        //                             card-frame control templates.
        //  4. UpdateLayout()         - third pass: flush deferred PropertyChanged
        //                             from template initialization.
        //  5. Dispatcher flush       - pump Background-and-above priority items
        //                             so any deferred ArtInFrameToBrushConverter
        //                             Binding evaluations (triggered by DataContext
        //                             change) complete before Render().
        //  6. UpdateLayout()         - final pass in case the art flush triggered
        //                             further invalidations.
        var fe = (System.Windows.FrameworkElement)s_selectorInstance;
        fe.Measure (new System.Windows.Size(gridW, gridH));
        fe.Arrange (new System.Windows.Rect(0, 0, gridW, gridH));
        fe.UpdateLayout(); // second pass: measure+arrange the new children
        fe.UpdateLayout(); // third pass: flush deferred PropertyChanged from template init

        // Pump the dispatcher down to Background priority to let deferred
        // Binding evaluations (art loading) complete before we capture.
        var artDispatcher = System.Windows.Application.Current?.Dispatcher;
        if (artDispatcher != null && artDispatcher.CheckAccess())
        {
          var flushFrame = new System.Windows.Threading.DispatcherFrame(exitWhenRequested: true);
          artDispatcher.BeginInvoke(
            new Action(() => flushFrame.Continue = false),
            System.Windows.Threading.DispatcherPriority.Background);
          System.Windows.Threading.Dispatcher.PushFrame(flushFrame);
        }

        fe.UpdateLayout(); // final pass after art-loading flush

        // Render via MTGO's own card frame controls into an off-screen bitmap
        var renderTarget = new RenderTargetBitmap(
          (int)gridW, (int)gridH, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render((Visual)s_selectorInstance);

        // Encode directly to PNG (no ReadProcessMemory needed — we're in-process)
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(renderTarget));
        enc.Save(ms);
        png = ms.ToArray();
      }
      finally
      {
        if (s_instantRenderProp != null)
          s_instantRenderProp.SetValue(null, prevInstantRender);

        // Release the ItemsSource reference so the selector doesn't hold
        // the grouping alive after the call returns.
        s_itemsSourceProp?.SetValue(s_selectorInstance, null);
      }
    });

    return png ?? Array.Empty<byte>();
  }

  // Threading helper

  /// <summary>
  /// Executes an action on an STA thread, blocking the caller until it completes.
  /// </summary>
  private static void RunOnSTA(Action action)
  {
    if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
    {
      action();
      return;
    }

    // Try the WPF dispatcher first (MTGO's UI thread is already STA)
    var app = System.Windows.Application.Current;
    if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
    {
      Exception ex = null;
      using var done = new ManualResetEventSlim(false);
      app.Dispatcher.BeginInvoke(new Action(() =>
      {
        try { action(); }
        catch (Exception e) { ex = e; }
        finally { done.Set(); }
      }));
      done.Wait();
      if (ex != null) throw ex;
      return;
    }

    // Final fallback: spin a fresh STA thread
    Exception threadEx = null;
    var t = new Thread(() =>
    {
      try { action(); }
      catch (Exception e) { threadEx = e; }
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
    if (threadEx != null) throw threadEx;
  }

  // Pixel blit / scale

  /// <summary>
  /// Nearest-neighbour scale-blit of src into dst at the given cell offset. Both buffers are Bgra32.
  /// </summary>
  private static void BlitScaled(
    CardArtPixels src,
    int dstW, int dstH,
    byte[] dst, int dstStridePixels,
    int dstX, int dstY)
  {
    float xScale = (float)src.Width  / dstW;
    float yScale = (float)src.Height / dstH;

    for (int y = 0; y < dstH; y++)
    {
      int srcY = (int)(y * yScale);
      int dstRowBase = ((dstY + y) * dstStridePixels + dstX) * 4;

      for (int x = 0; x < dstW; x++)
      {
        int srcX = (int)(x * xScale);
        int srcOffset = (srcY * src.Width + srcX) * 4;
        int dstOffset = dstRowBase + x * 4;

        dst[dstOffset + 0] = src.Pixels[srcOffset + 0]; // B
        dst[dstOffset + 1] = src.Pixels[srcOffset + 1]; // G
        dst[dstOffset + 2] = src.Pixels[srcOffset + 2]; // R
        dst[dstOffset + 3] = src.Pixels[srcOffset + 3]; // A
      }
    }
  }
}

/// <summary>
/// Plain-data holder for decoded Bgra32 pixel data from a card art image.
/// </summary>
public readonly struct CardArtPixels
{
  /// <summary>Raw Bgra32 pixel buffer, row-major.</summary>
  public readonly byte[] Pixels;

  /// <summary>Width in pixels.</summary>
  public readonly int Width;

  /// <summary>Height in pixels.</summary>
  public readonly int Height;

  public CardArtPixels(byte[] pixels, int width, int height)
  {
    Pixels = pixels;
    Width  = width;
    Height = height;
  }
}
#endif // NETFRAMEWORK
