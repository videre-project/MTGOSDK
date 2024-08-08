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
using static MTGOSDK.Core.Reflection.DLRWrapper;

using WotC.MtGO.Client.Model.Core;
using WotC.MtGO.Client.Model.Settings;
using WotC.MtGO.Client.Model.Settings.History;


namespace MTGOSDK.API.Play.History;

/// <summary>
/// Manager for the player's game history.
/// </summary>
public static class HistoryManager
{
  /// <summary>
  /// Global manager for the player's game history.
  /// </summary>
  private static readonly IGameHistoryManager s_gameHistoryManager =
    ObjectProvider.Get<ISettings>().GameHistoryManager;

  /// <summary>
  /// The MTGO client's ISO serializer for reading and writing binary objects.
  /// </summary>
  private static readonly IIsoSerializer s_isoSerializer =
    ObjectProvider.Get<IIsoSerializer>();

  /// <summary>
  /// The method used to load the ISO configuration for reading binary objects.
  /// </summary>
  private static readonly dynamic s_loadIsoConfiguration =
    Unbind(s_gameHistoryManager).LoadIsoConfigration;

  /// <summary>
  /// The serialization binder used to deserialize the game history file.
  /// </summary>
  private static readonly dynamic s_serializationBinder =
    RemoteClient.CreateInstance(new TypeProxy<HistoricalSerializationBinder>());

  /// <summary>
  /// The file name of the game history file.
  /// </summary>
  private static readonly string s_gameHistoryFile = "mtgo_game_history";

  //
  // IGameHistoryManager wrapper properties
  //

  /// <summary>
  /// Whether or not the game history has been loaded by the client.
  /// </summary>
  public static bool HistoryLoaded => s_gameHistoryManager.HistoryLoaded;

  /// <summary>
  /// The historical items (games, matches, tournaments, etc.) for the player.
  /// </summary>
  public static IList<dynamic> Items =>
    Map<IList, dynamic>(s_gameHistoryManager.Items, HistoricalEventFactory);

  //
  // IGameHistoryManager wrapper methods
  //

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
      s_gameHistoryFile,
      s_loadIsoConfiguration,
      username,
      s_serializationBinder
    );

    var gameHistory = Bind<IList<IHistoricalItem>>(binaryObject);
    Log.Debug("Read {Count} items from game history.", gameHistory.Count);

    return Map<IList, dynamic>(gameHistory, HistoricalEventFactory);
  }

  /// <summary>
  /// Gets the game history files for a given player.
  /// </summary>
  /// <param name="username">The username of the player.</param>
  /// <param name="filterFiles">
  /// Whether to only search for and return existing game history files.
  /// </param>
  /// <returns>The file paths to the player's game history files.</returns>
  public static string[] GetGameHistoryFiles(
    string? username = null,
    bool filterFiles = true)
  {
    // Default to the current user if no username is provided.
    if (string.IsNullOrEmpty(username))
      username = Client.CurrentUser.Name;
    Log.Information("Getting game history files for {Username}.", username);

    // Get a list of existing MTGO data directories on the system.
    string userPattern = GetUserHash(username);
    string[] dataDirectories = new Glob(Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      @"Apps\2.0\Data\**\mtgo..tion_*\**\AppFiles"
    ));

    return Map<string>(dataDirectories.OrderByDescending(File.GetLastWriteTime),
                       (d) => Path.Combine(d, userPattern, s_gameHistoryFile))
      .Where(f => !filterFiles || File.Exists(f))
      .ToArray();
  }

  /// <summary>
  /// Merges a game history file with the current game history collection.
  /// </summary>
  /// <param name="filePath">The path to the 'mtgo_game_history' file.</param>
  /// <param name="save">Whether or not to save the merged collection.</param>
  /// <returns>The merged game history collection.</returns>
  public static IList<dynamic> MergeGameHistory(string filePath, bool save = true)
  {
    //
    // Use a reserved username to mark a temporary user directory.
    //
    // MTGO uses this MD5 hash to generate a user-specific directory read by
    // the <see cref="ReadGameHistory"/> method. We can use the same hash to
    // reserve a temporary user directory with a copy of the game history file.
    //
    string mockUsername = $"$RESERVED-{Guid.NewGuid().ToString("N")[..7]}";
    string temporaryFile = GetGameHistoryFiles(mockUsername, false).First();
    string temporaryDataDir = Path.GetDirectoryName(temporaryFile);
    Log.Trace("Using temporary user directory: {TemporaryDataDir}", temporaryDataDir);

    // Copy the game history file to the temporary user directory.
    Directory.CreateDirectory(temporaryDataDir);
    File.Copy(filePath, temporaryFile);

    // Read the game history file into a new collection in client memory.
    IEnumerable<dynamic> gameHistory = ReadGameHistory(mockUsername);
    Directory.Delete(temporaryDataDir, recursive: true);
    Log.Trace("Read {Count} items from game history file: {FilePath}",
        gameHistory.Count(), filePath);

    // Merge the new game history with the current game history collection.
    Log.Information("Merging game history with current collection.");
    var itemIds = new HashSet<int>(Items.Select(item => (int)item.Id));
    foreach (var item in gameHistory)
    {
      // Skip items that are already in the current game history collection.
      if (itemIds.Contains(item.Id)) continue;

      dynamic dro = Unbind(item); // Get handle to the remote object.
      if (dro.LoadSuccessfull)
        Unbind(s_gameHistoryManager).Items.Add(dro);
    }

    // Update the client's history load status if not already set.
    if (!s_gameHistoryManager.HistoryLoaded)
      Unbind(s_gameHistoryManager).HistoryLoaded |= true;

    // Save the merged game history collection to the local game history file.
    if (save)
      Unbind(s_gameHistoryManager).SaveItems();

    return Items;
  }

  /// <summary>
  /// Gets the pattern used to generate a user-specific data directory.
  /// </summary>
  /// <param name="username">The username of the player.</param>
  /// <returns>The user-specific hash pattern.</returns>
  private static string GetUserHash(string username) =>
    string.Join("", MD5.HashData(Encoding.ASCII.GetBytes(username.ToLower()))
      .Select(b => b.ToString("X2")));

  //
  // IHistoricalItem helper methods
  //

  private static readonly Func<dynamic, dynamic> HistoricalEventFactory =
    new(CastHistoricalItem);

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
