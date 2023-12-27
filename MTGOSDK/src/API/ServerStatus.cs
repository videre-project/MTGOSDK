/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;


namespace MTGOSDK.API;

/// <summary>
/// Check the status of the MTGO servers.
/// </summary>
public static class ServerStatus
{
  /// <summary>
  /// Check if the MTGO servers are online.
  /// </summary>
  public static async Task<bool> IsOnline()
  {
    using (HttpClient client = new HttpClient())
    {
      string url = "https://census.daybreakgames.com/s:dgc/get/global/game_server_status?game_code=mtgo&c:limit=1000";
      using var response = await client.GetAsync(url);

      if (!response.IsSuccessStatusCode)
        throw new Exception("Failed to fetch server status");

      using var content = response.Content;
      var json = JObject.Parse(await content.ReadAsStringAsync());

      if (json["returned"].ToObject<int>() == 0)
        throw new Exception("No servers found");

      // Check if any servers are online.
      IList<string> statuses = [ "high", "medium", "low" ];
      return json["game_server_status_list"].Any(s =>
          statuses.Contains(s["last_reported_state"].ToObject<string>()));
    }
  }
}
