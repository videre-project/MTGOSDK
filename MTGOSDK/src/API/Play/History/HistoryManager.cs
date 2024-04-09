/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Win32.Utilities.FileSystem;

using WotC.MtGO.Client.Model;
using WotC.MtGO.Client.Model.Core;
using WotC.MtGO.Client.Model.Settings;


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
  private static IGameHistoryManager s_gameHistoryManager =
    ObjectProvider.Get<ISettings>().GameHistoryManager;

  /// <summary>
  /// Whether or not the game history has been loaded by the client.
  /// </summary>
  public static bool HistoryLoaded => s_gameHistoryManager.HistoryLoaded;

  /// <summary>
  /// The historical items (games, matches, tournaments, etc.) for the player.
  /// </summary>
  public static IEnumerable<dynamic> Items =>
    Map<dynamic>(
      s_gameHistoryManager.Items,
      new Func<dynamic, dynamic>(item => CastHistoricalItem(item))
    );

  /// <summary>
  /// Reads the local game history for a given player.
  /// </summary>
  /// <param name="username">The username of the player.</param>
  /// <returns>The player's game history.</returns>
  public static IEnumerable<dynamic> ReadGameHistory(string? username = null)
  {
    // Default to the current user if no username is provided.
    if (string.IsNullOrEmpty(username))
      username = Unbind(s_gameHistoryManager).m_session.CurrentUser.Name;

    var serializer = ObjectProvider.Get<IIsoSerializer>(bindTypes: false);
    dynamic serializationBinder = RemoteClient.CreateInstance(
      "WotC.MtGO.Client.Model.Settings.History.HistoricalSerializationBinder");

    var gameHistory = serializer.ReadBinaryObject<dynamic>("mtgo_game_history",
      Unbind(s_gameHistoryManager).LoadIsoConfigration,
      username,
      serializationBinder
    );

    return Map<dynamic>(
      gameHistory,
      new Func<dynamic, dynamic>(item => CastHistoricalItem(item))
    );
  }

  /// <summary>
  /// Merges a game history file with the current game history collection.
  /// </summary>
  /// <param name="filePath">The path to the 'mtgo_game_history' file.</param>
  /// <returns>The merged game history collection.</returns>
  public static IEnumerable<dynamic> MergeGameHistory(string filePath)
  {
    // Find the user data directory for the current MTGO installation.
    string currentDataDir = new Glob(Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      @"Apps\2.0\Data\**\mtgo..tion_*\**\AppFiles"
    ));

    //
    // Use a reserved username to mark a temporary user directory.
    //
    // MTGO uses this MD5 hash to generate a user-specific directory read by
    // the <see cref="ReadGameHistory"/> method. We can use the same hash to
    // reserve a temporary user directory with a copy of the game history file.
    //
    string mockUsername = $"$RESERVED-{Guid.NewGuid().ToString("N")[..7]}";
    byte[] usernameBytes = Encoding.ASCII.GetBytes(mockUsername.ToLower());
    byte[] usernameBytesHash = MD5.Create().ComputeHash(usernameBytes);

    string temporaryDataDir = Path.Combine(currentDataDir,
      String.Join("", usernameBytesHash.Select(b => b.ToString("X2")))
    );

    // Copy the game history file to the temporary user directory.
    Directory.CreateDirectory(temporaryDataDir);
    File.Copy(filePath, Path.Combine(temporaryDataDir, "mtgo_game_history"));

    // Read the game history file into a new collection in client memory.
    IEnumerable<dynamic> gameHistory = ReadGameHistory(mockUsername);
    Directory.Delete(temporaryDataDir, recursive: true);

    // Merge the new game history with the current game history collection.
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
    Unbind(s_gameHistoryManager).SaveItems();
    Unbind(s_gameHistoryManager).HistoryLoaded |= true;

    return Items;
  }

  //
  // IHistoricalItem helper methods
  //

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
    var type = item.GetType().Name;
    switch (type)
    {
      case "HistoricalItem":
        return new HistoricalItem<IHistoricalItem, dynamic>.Default(item);
      case "HistoricalMatch":
        return new HistoricalMatch(item);
      case "HistoricalTournament":
        return new HistoricalTournament(item);
      default:
        throw new NotImplementedException($"Unsupported type: {type}");
    }
  }
}
