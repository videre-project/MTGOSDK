/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections;
using System.IO;
using System.Text;
using System.Security.Cryptography;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Win32.FileSystem;

using WotC.MtGO.Client.Model.Core;
using WotC.MtGO.Client.Model.Settings;
using WotC.MtGO.Client.Model.Settings.History;


namespace MTGOSDK.API.Play.History;
using static MTGOSDK.Core.Reflection.DLRWrapper<dynamic>;

/// <summary>
/// Manager for the player's game history.
/// </summary>
public static class HistoryManager
{
  //
  // IGameHistoryManager wrapper methods
  //

  /// <summary>
  /// Global manager for the player's game history.
  /// </summary>
  private static readonly IGameHistoryManager s_gameHistoryManager =
    ObjectProvider.Get<ISettings>().GameHistoryManager;

  private static readonly IIsoSerializer s_isoSerializer =
    ObjectProvider.Get<IIsoSerializer>();

  private static readonly dynamic s_loadIsoConfiguration =
    Unbind(s_gameHistoryManager).LoadIsoConfigration;

  private static readonly dynamic s_serializationBinder =
    RemoteClient.CreateInstance(new TypeProxy<HistoricalSerializationBinder>());

  /// <summary>
  /// Whether or not the game history has been loaded by the client.
  /// </summary>
  public static bool HistoryLoaded => s_gameHistoryManager.HistoryLoaded;

  /// <summary>
  /// The historical items (games, matches, tournaments, etc.) for the player.
  /// </summary>
  public static IList<dynamic> Items =>
    Map<IList, dynamic>(s_gameHistoryManager.Items, HistoricalEventFactory);

  /// <summary>
  /// Reads the local game history for a given player.
  /// </summary>
  /// <param name="username">The username of the player.</param>
  /// <returns>The player's game history.</returns>
  public static IList<dynamic> ReadGameHistory(string? username = null)
  {
    // Default to the current user if no username is provided.
    if (string.IsNullOrEmpty(username))
      username = Client.CurrentUser.Name;
    Log.Information("Reading game history for {Username}.", username);

    object binaryObject = Unbind(s_isoSerializer).ReadBinaryObject<dynamic>(
      "mtgo_game_history",
      s_loadIsoConfiguration,
      username,
      s_serializationBinder
    );

    var gameHistory = Bind<IList<IHistoricalItem>>(binaryObject);
    Log.Debug("Read {Count} items from game history.", gameHistory.Count);

    return Map<IList, dynamic>(gameHistory, HistoricalEventFactory);
  }

  /// <summary>
  /// Merges a game history file with the current game history collection.
  /// </summary>
  /// <param name="filePath">The path to the 'mtgo_game_history' file.</param>
  /// <param name="save">Whether or not to save the merged collection.</param>
  /// <returns>The merged game history collection.</returns>
  public static IList<dynamic> MergeGameHistory(string filePath, bool save = true)
  {
    // Find the user data directory for the current MTGO installation.
    string currentDataDir = new Glob(Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      @"Apps\2.0\Data\**\mtgo..tion_*\**\AppFiles"
    ));
    Log.Trace("Found MTGO data directory: {CurrentDataDir}", currentDataDir);

    //
    // Use a reserved username to mark a temporary user directory.
    //
    // MTGO uses this MD5 hash to generate a user-specific directory read by
    // the <see cref="ReadGameHistory"/> method. We can use the same hash to
    // reserve a temporary user directory with a copy of the game history file.
    //
    string mockUsername = $"$RESERVED-{Guid.NewGuid().ToString("N")[..7]}";
    byte[] usernameBytes = Encoding.ASCII.GetBytes(mockUsername.ToLower());
    byte[] usernameBytesHash = MD5.HashData(usernameBytes);

    string temporaryDataDir = Path.Combine(currentDataDir,
      string.Join("", usernameBytesHash.Select(b => b.ToString("X2")))
    );

    // Copy the game history file to the temporary user directory.
    Directory.CreateDirectory(temporaryDataDir);
    File.Copy(filePath, Path.Combine(temporaryDataDir, "mtgo_game_history"));

    // Read the game history file into a new collection in client memory.
    IEnumerable<dynamic> gameHistory = ReadGameHistory(mockUsername);
    Directory.Delete(temporaryDataDir, recursive: true);

    // Merge the new game history with the current game history collection.
    Log.Information("Merging game history with current collection.");
    var itemIds = new HashSet<int>(Items.Select(item => (int)item.Id));
    foreach (var item in gameHistory)
    {
      // Skip items that are already in the current game history collection.
      if (itemIds.Contains(item.Id)) continue;

      dynamic dro = Unbind(item.@base); // Get handle to the remote object.
      if (dro.LoadSuccessfull)
        Unbind(s_gameHistoryManager).Items.Add(dro);
    }

    // Save the merged game history collection to the local game history file.
    if (save) Unbind(s_gameHistoryManager).SaveItems();
    Unbind(s_gameHistoryManager).HistoryLoaded |= true;

    return Items;
  }

  //
  // IHistoricalItem helper methods
  //

  internal static Func<dynamic, dynamic> HistoricalEventFactory = new(CastHistoricalItem);

  /// <summary>
  /// Cast a historical item to it's implementation subclass.
  /// </summary>
  /// <param name="item">The historical item to cast.</param>
  /// <returns>
  /// The casted historical item (i.e. HistoricalMatch, HistoricalTournament).
  /// </returns>
  /// <exception cref="NotImplementedException">
  /// Thrown when the item type is not supported.
  /// </exception>
  private static dynamic CastHistoricalItem(dynamic item)
  {
    // Extract the underlying historical item from any proxied objects.
    dynamic historicalItem = Unbind(item);

    // If an event is provided as a FilterableEvent, extract the actual event.
    string eventType = historicalItem.GetType().Name;
    dynamic eventObject = eventType.StartsWith("Filterable")
      ? historicalItem.PlayerEvent
      : historicalItem;

    switch (eventType)
    {
      case "HistoricalItem":
        eventObject = new HistoricalItem<dynamic>.Default(eventObject);
        break;
      case "HistoricalMatch":
        eventObject = new HistoricalMatch(eventObject);
        break;
      case "HistoricalTournament":
        eventObject = new HistoricalTournament(eventObject);
        break;
      default:
        throw new NotImplementedException($"Unsupported type: {eventType}");
    }
    Log.Trace("Creating new {Type} object for item id #{HistoricalObject}",
        eventObject.GetType(), eventObject.Id);

    return eventObject;
  }
}
