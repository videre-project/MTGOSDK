/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using WotC.MtGO.Client.Model.ResourceManagement;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Remoting;

using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace MTGOSDK.API.Graphics;

/// <summary>
/// Renders MTGO card images using the in-process WPF pipeline exposed by
/// <c>CardRenderingHelpers</c>.
/// </summary>
public static class CardRenderer
{
  /// <summary>MTGO card aspect ratio (5 : 7 = width : height).</summary>
  private const double CardAspectRatio = 5.0 / 7.0;

  //
  // Card art helpers
  //

  /// <summary>
  /// Ensures the card's visual resource is fully downloaded and returns the
  /// local file path to the art image.
  /// </summary>
  private static async Task<string?> EnsureCardArtLoaded(Card card)
  {
    dynamic cardDef  = Unbind(card);
    dynamic resource = cardDef.Resource;
    if (resource == null) return null;

    var loadState = Cast<VisualResourceLoadState>(resource.LoadState);
    if (loadState != VisualResourceLoadState.Loaded)
    {
      var downloadTask = resource.DownloadResourceAsync();
      await downloadTask;
    }

    var viewUri = resource.View;
    if (viewUri == null || viewUri.OriginalString == ".") return null;

    return viewUri.IsFile ? viewUri.LocalPath : null;
  }

  /// <summary>
  /// Returns the local file path to a card's art image, downloading it from
  /// the CDN if necessary.
  /// </summary>
  /// <param name="card">The card to fetch art for.</param>
  /// <returns>Absolute path, or <c>null</c> if the art could not be resolved.</returns>
  public static async Task<string?> GetCardArtPath(Card card)
  {
    var localPath = await EnsureCardArtLoaded(card);
    if (localPath == null || !File.Exists(localPath)) return null;
    return localPath;
  }

  /// <summary>
  /// Returns the raw bytes of a card's art image file.
  /// </summary>
  /// <param name="card">The card to fetch art for.</param>
  /// <returns>File bytes, or <c>null</c> if unavailable.</returns>
  public static async Task<byte[]?> GetCardArtBytes(Card card)
  {
    var path = await GetCardArtPath(card);
    if (path == null) return null;
    return await File.ReadAllBytesAsync(path);
  }

  //
  // Batched framed rendering
  //

  /// <summary>
  /// Renders a set of cards as fully-framed images using a single WPF render
  /// pass, then yields one completed grid row at a time so callers can stream
  /// results incrementally without triggering a second render.
  /// </summary>
  /// <param name="catalogIds">Catalog IDs to render, in desired output order.</param>
  /// <param name="columns">Grid column count. Default: 5.</param>
  /// <param name="cardHeight">Per-card height in pixels. Default: 300.</param>
  /// <returns>
  ///   An enumerable where each element is an array of up to
  ///   <paramref name="columns"/> base64 PNG strings for that row, in input
  ///   order.  The array length equals <paramref name="columns"/> for full rows
  ///   and may be shorter for the final row.
  /// </returns>
  public static IEnumerable<string[]> RenderCardsRowByRow(
    int[] catalogIds,
    int columns    = 5,
    int cardHeight = 300)
  {
    if (catalogIds == null || catalogIds.Length == 0)
      yield break;

    // Single IPC call: one full WPF render pass for all cards.
    string layoutResult = (string)RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CardRenderingHelpers",
      "RenderCardIdsToFramedGridPngWithLayout",
      null,
      string.Join(",", catalogIds),
      columns,
      cardHeight,
      8000);

    if (string.IsNullOrEmpty(layoutResult))
      yield break;

    int pipe = layoutResult.IndexOf('|');
    if (pipe < 0) yield break;

    string slotCsv   = layoutResult[..pipe];
    string base64Png = layoutResult[(pipe + 1)..];
    if (string.IsNullOrEmpty(slotCsv) || string.IsNullOrEmpty(base64Png))
      yield break;

    // Build the catalogId → slot-index mapping once.
    int[] slotOrder = slotCsv
      .Split(',')
      .Select(s => int.TryParse(s, out int n) ? n : 0)
      .ToArray();

    var slotsByCard = new Dictionary<int, Queue<int>>(slotOrder.Length);
    for (int i = 0; i < slotOrder.Length; i++)
    {
      int catId = slotOrder[i];
      if (!slotsByCard.TryGetValue(catId, out var q))
        slotsByCard[catId] = q = new Queue<int>();
      q.Enqueue(i);
    }

    var slotForIndex = new int[catalogIds.Length];
    for (int i = 0; i < catalogIds.Length; i++)
    {
      int catId = catalogIds[i];
      slotForIndex[i] = (slotsByCard.TryGetValue(catId, out var q) && q.Count > 0)
        ? q.Dequeue()
        : -1;
    }

    // Decode the full sheet to pixels once — all crop operations read from this.
    int cardWidth = (int)Math.Ceiling(cardHeight * CardAspectRatio);
    byte[] pngBytes = Convert.FromBase64String(base64Png);
    byte[] sheetPx  = DecodePngToPixels(pngBytes, out int sheetW, out _);

    // Yield one complete row at a time.  Each row's cards are cropped in
    // parallel, then the whole row is yielded before moving to the next.
    int total = catalogIds.Length;
    for (int rowStart = 0; rowStart < total; rowStart += columns)
    {
      int rowEnd  = Math.Min(rowStart + columns, total);
      int rowLen  = rowEnd - rowStart;
      var rowBase64 = new string[rowLen];

      Parallel.For(0, rowLen, j =>
      {
        int absIdx = rowStart + j;
        int slot   = slotForIndex[absIdx];
        rowBase64[j] = slot >= 0
          ? CropSlotToBase64(sheetPx, sheetW, slot % columns, slot / columns, cardWidth, cardHeight)
          : string.Empty;
      });

      yield return rowBase64;
    }
  }

  /// <summary>
  /// Renders a set of cards (identified by catalog ID) as fully-framed card
  /// images and returns each as a base64-encoded PNG string.
  /// </summary>
  public static string[] RenderCards(
    int[] catalogIds,
    int columns    = 5,
    int cardHeight = 300)
  {
    if (catalogIds == null || catalogIds.Length == 0)
      return Array.Empty<string>();

    // Single IPC call: render sheet in MTGO process + return slot order.
    // Return format: "catalogId1,catalogId2,...|<base64png>"
    string layoutResult = (string)RemoteClient.InvokeMethod(
      "MTGOSDK.Core.Remoting.Interop.CardRenderingHelpers",
      "RenderCardIdsToFramedGridPngWithLayout",
      null,
      string.Join(",", catalogIds),
      columns,
      cardHeight,
      8000);

    if (string.IsNullOrEmpty(layoutResult))
      return new string[catalogIds.Length];

    int pipe = layoutResult.IndexOf('|');
    if (pipe < 0)
      return new string[catalogIds.Length];

    string slotCsv   = layoutResult[..pipe];
    string base64Png = layoutResult[(pipe + 1)..];

    if (string.IsNullOrEmpty(slotCsv) || string.IsNullOrEmpty(base64Png))
      return new string[catalogIds.Length];

    // slotOrder[i] = catalog ID of the card at rendered slot i
    int[] slotOrder = slotCsv
      .Split(',')
      .Select(s => int.TryParse(s, out int n) ? n : 0)
      .ToArray();

    // catalogId → queue of slot indices (handles duplicate IDs in input)
    var slotsByCard = new Dictionary<int, Queue<int>>(slotOrder.Length);
    for (int i = 0; i < slotOrder.Length; i++)
    {
      int catId = slotOrder[i];
      if (!slotsByCard.TryGetValue(catId, out var q))
        slotsByCard[catId] = q = new Queue<int>();
      q.Enqueue(i);
    }

    // Decode the full sheet to raw Bgra32 pixels (all in the SDK process)
    byte[] pngBytes  = Convert.FromBase64String(base64Png);
    int cardWidth    = (int)Math.Ceiling(cardHeight * CardAspectRatio);
    byte[] sheetPx   = DecodePngToPixels(pngBytes, out int sheetW, out _);

    // Pre-compute each input index → sheet slot index (sequential so Queue.Dequeue
    // is never called from multiple threads for the same duplicate catalogId).
    var slotForIndex = new int[catalogIds.Length];
    for (int i = 0; i < catalogIds.Length; i++)
    {
      int catId = catalogIds[i];
      slotForIndex[i] = (slotsByCard.TryGetValue(catId, out var q) && q.Count > 0)
        ? q.Dequeue()
        : -1;
    }

    // Crop + encode each card in parallel.
    // CropSlotToBase64 reads from the immutable sheetPx buffer and writes to its
    // own result slot — no shared mutable state, fully thread-safe.
    var result = new string[catalogIds.Length];
    Parallel.For(0, catalogIds.Length, i =>
    {
      int slot = slotForIndex[i];
      result[i] = slot >= 0
        ? CropSlotToBase64(sheetPx, sheetW, slot % columns, slot / columns, cardWidth, cardHeight)
        : string.Empty;
    });

    return result;
  }

  //
  // Local image processing helpers
  //

  /// <summary>
  /// Decodes a PNG byte array to a flat Bgra32 pixel buffer.
  /// </summary>
  private static byte[] DecodePngToPixels(byte[] png, out int width, out int height)
  {
    using var stream = new MemoryStream(png);
    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    BitmapSource frame = decoder.Frames[0];

    // Normalise to Bgra32 so the stride is always width * 4
    if (frame.Format != PixelFormats.Bgra32)
      frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

    width  = frame.PixelWidth;
    height = frame.PixelHeight;
    var pixels = new byte[width * height * 4];
    frame.CopyPixels(pixels, width * 4, 0);
    return pixels;
  }

  /// <summary>
  /// Copies a single card-sized region out of the sheet pixel buffer and
  /// encodes it as a base64 PNG string using a thread-safe managed encoder.
  /// </summary>
  private static string CropSlotToBase64(
    byte[] sheet, int sheetWidth,
    int col, int row,
    int cardWidth, int cardHeight)
  {
    int srcStride = sheetWidth * 4;
    int dstStride = cardWidth  * 4;
    int srcX      = col * cardWidth  * 4;  // byte offset of the left column edge
    int srcY      = row * cardHeight;      // top row of this slot

    var bgra = new byte[dstStride * cardHeight];
    for (int y = 0; y < cardHeight; y++)
      Buffer.BlockCopy(sheet, (srcY + y) * srcStride + srcX, bgra, y * dstStride, dstStride);

    return Convert.ToBase64String(EncodePixelsToPng(bgra, cardWidth, cardHeight));
  }

  // ── Managed PNG encoder (thread-safe, no WPF dependency) ─────────────────

  /// <summary>
  /// Precomputed CRC-32 lookup table used by the PNG chunk writer.
  /// </summary>
  private static readonly uint[] s_crc32Table = BuildCrc32Table();

  private static uint[] BuildCrc32Table()
  {
    var t = new uint[256];
    for (uint i = 0; i < 256; i++)
    {
      uint c = i;
      for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
      t[i] = c;
    }
    return t;
  }

  private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
  {
    uint crc = 0xFFFFFFFF;
    foreach (byte b in type) crc = (crc >> 8) ^ s_crc32Table[(crc ^ b) & 0xFF];
    foreach (byte b in data) crc = (crc >> 8) ^ s_crc32Table[(crc ^ b) & 0xFF];
    return crc ^ 0xFFFFFFFF;
  }

  private static void WriteBigEndian(Stream s, uint v)
  {
    s.WriteByte((byte)(v >> 24));
    s.WriteByte((byte)(v >> 16));
    s.WriteByte((byte)(v >> 8));
    s.WriteByte((byte)v);
  }

  /// <summary>
  /// Encodes a Bgra32 pixel buffer as a PNG byte array.
  /// Thread-safe: creates no WPF objects, safe to call from <c>Parallel.For</c>.
  /// </summary>
  private static byte[] EncodePixelsToPng(byte[] bgra, int width, int height)
  {
    // Build IDAT payload: zlib-compressed RGBA scanlines (BGRA → RGBA swap inline).
    byte[] idatData;
    using (var idatBuf = new MemoryStream())
    {
      using (var zlib = new ZLibStream(idatBuf, CompressionLevel.Fastest, leaveOpen: true))
      {
        int stride = width * 4;
        var row = new byte[1 + stride]; // byte 0 = filter type (None = 0)
        for (int y = 0; y < height; y++)
        {
          int src = y * stride;
          for (int x = 0, d = 1; x < width; x++, d += 4)
          {
            // BGRA memory layout: B=+0, G=+1, R=+2, A=+3
            // PNG color type 6 (RGBA) expects: R, G, B, A
            row[d]     = bgra[src + x * 4 + 2]; // R
            row[d + 1] = bgra[src + x * 4 + 1]; // G
            row[d + 2] = bgra[src + x * 4 + 0]; // B
            row[d + 3] = bgra[src + x * 4 + 3]; // A
          }
          zlib.Write(row);
        }
      } // Dispose() flushes zlib checksum footer
      idatData = idatBuf.ToArray();
    }

    // Estimated output size: signature(8) + IHDR(25) + IDAT(12+data) + IEND(12)
    using var ms = new MemoryStream(idatData.Length + 57);

    // PNG signature
    ms.Write((ReadOnlySpan<byte>)new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    // Local helper: write one chunk (length | type | data | CRC32)
    void WriteChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
      WriteBigEndian(ms, (uint)data.Length);
      ms.Write(type);
      ms.Write(data);
      WriteBigEndian(ms, ComputeCrc32(type, data));
    }

    // IHDR (13 data bytes)
    Span<byte> ihdr = stackalloc byte[13];
    ihdr[0] = (byte)(width  >> 24); ihdr[1] = (byte)(width  >> 16);
    ihdr[2] = (byte)(width  >>  8); ihdr[3] = (byte)width;
    ihdr[4] = (byte)(height >> 24); ihdr[5] = (byte)(height >> 16);
    ihdr[6] = (byte)(height >>  8); ihdr[7] = (byte)height;
    ihdr[8]  = 8; // bit depth: 8
    ihdr[9]  = 6; // color type: RGBA
    ihdr[10] = 0; // compression: deflate
    ihdr[11] = 0; // filter method: adaptive
    ihdr[12] = 0; // interlace: none
    WriteChunk("IHDR"u8, ihdr);

    // IDAT
    WriteChunk("IDAT"u8, idatData);

    // IEND (no data)
    WriteChunk("IEND"u8, ReadOnlySpan<byte>.Empty);

    return ms.ToArray();
  }
}
