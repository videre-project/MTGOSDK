/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Net.Http;
using System.Text;

using Newtonsoft.Json;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// The reverse communicator is used by the Diver to communicate back with its
/// controller regarding callbacks invocations
/// </summary>
public class ReverseCommunicator
{
  private readonly string _hostname;
  private readonly int _port;

  private static readonly HttpClient _client = new()
  {
    Timeout = TimeSpan.FromSeconds(5)
  };

  public ReverseCommunicator(string hostname, int port)
  {
    _hostname = hostname;
    _port = port;
  }
  public ReverseCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) {}
  public ReverseCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) {}

  public static void Dispose() => _client.Dispose();

  private async Task<string?> SendRequestAsync(
    string path,
    Dictionary<string, string> queryParams = null,
    string jsonBody = null,
    bool ignoreResponse = false)
  {
    queryParams ??= new();

    string query = "";
    bool firstParam = true;
    foreach (KeyValuePair<string, string> kvp in queryParams)
    {
      query += $"{(firstParam ? "?" : "&")}{kvp.Key}={kvp.Value}";
      firstParam = false;
    }

    string url = $"http://{_hostname}:{_port}/{path}" + query;
    HttpRequestMessage msg;
    if (jsonBody == null)
    {
      msg = new HttpRequestMessage(HttpMethod.Get, url);
    }
    else
    {
      msg = new HttpRequestMessage(HttpMethod.Post, url)
      {
        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
      };
    }

    try
    {
      HttpResponseMessage res = await _client.SendAsync(msg);

      if (!ignoreResponse)
      {
        // Read the response body only if not ignoring the response
        return await res.Content.ReadAsStringAsync();
      }
    }
    catch (Exception ex)
    {
      // Log the exception if needed
      Log.Error($"Failed to send request: {ex.Message}");
      if (!ignoreResponse)
      {
        throw; // Re-throw the exception if the response is required
      }
    }

    return null;
  }

  public bool CheckIfAlive()
  {
    try
    {
      var resJson = SendRequestAsync("ping").GetAwaiter().GetResult();
      if (resJson == null)
        return false;

      return resJson.Contains("pong");
    }
    catch
    {
      return false;
    }
  }

  public void InvokeCallback(
    int token,
    DateTime timestamp,
    params ObjectOrRemoteAddress[] args)
  {
    CallbackInvocationRequest invocReq = new()
    {
      Timestamp = timestamp,
      Token = token,
      Parameters = args.ToList()
    };

    var requestJsonBody = JsonConvert.SerializeObject(invocReq);
    _ = SendRequestAsync("invoke_callback", null, requestJsonBody, true);
  }
}
