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
using static MTGOSDK.Core.Reflection.DLRWrapper;

namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// The reverse communicator is used by the Diver to communicate back with its
/// controller regarding callbacks invocations
/// </summary>
public class ReverseCommunicator
{
  private readonly string _hostname;
  private readonly int _port;

  private static readonly SemaphoreSlim s_semaphore =
    new(2 * Environment.ProcessorCount);

  private static readonly HttpClient s_client = new()
  {
    Timeout = TimeSpan.FromSeconds(5),
    DefaultRequestHeaders = { ConnectionClose = false }
  };

  public ReverseCommunicator(string hostname, int port)
  {
    _hostname = hostname;
    _port = port;
  }
  public ReverseCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) {}
  public ReverseCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) {}

  public static void Dispose() => s_client.Dispose();

  private async Task<string?> SendRequestAsync(
    string path,
    string jsonBody = null,
    bool ignoreResponse = false)
  {
    string url = $"http://{_hostname}:{_port}/{path}";
    try
    {
      await s_semaphore.WaitAsync();
      HttpResponseMessage res = await RetryAsync(
        async () =>
        {
          // Create a new HttpRequestMessage for each attempt
          using var msg = new HttpRequestMessage(
            jsonBody == null ? HttpMethod.Get : HttpMethod.Post,
            url);

          if (jsonBody != null)
          {
            msg.Content = new StringContent(
              jsonBody,
              Encoding.UTF8,
              "application/json");
          }

          return await s_client.SendAsync(msg);
        },
        delay: 10,
        raise: true);

      if (!ignoreResponse)
      {
        // Read the response body only if not ignoring the response
        return await res.Content.ReadAsStringAsync();
      }
    }
    catch (TaskCanceledException ex)
    {
      Log.Error($"Request timed out: {ex.Message}");
      Log.Debug(ex.StackTrace);
      if (!ignoreResponse) throw;
    }
    catch (Exception ex)
    {
      // Log the exception if needed
      Log.Error($"Failed to send request: {ex.Message}");
      Log.Debug(ex.StackTrace);
      if (!ignoreResponse) throw;
    }
    finally
    {
      s_semaphore.Release();
    }

    return null;
  }

  public async Task<bool> CheckIfAlive()
  {
    try
    {
      var resJson = await SendRequestAsync("ping");
      if (resJson == null)
        return false;

      return resJson.Contains("pong");
    }
    catch
    {
      return false;
    }
  }

  public async Task InvokeCallback(
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
    _ = await SendRequestAsync("invoke_callback", requestJsonBody, true);
  }
}
