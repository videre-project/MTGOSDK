/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;

using Newtonsoft.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// The reverse communicator is used by the Diver to communicate back with its
/// controller regarding callbacks invocations
/// </summary>
public class ReverseCommunicator : BaseCommunicator
{
  private static readonly JsonSerializerSettings s_jsonSettings = new()
  {
    TypeNameHandling = TypeNameHandling.None,
    DefaultValueHandling = DefaultValueHandling.Ignore
  };

  public ReverseCommunicator(
    string hostname,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
    : base(hostname, port, cancellationTokenSource) { }

  public ReverseCommunicator(
    IPAddress ipa,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
    : this(ipa.ToString(), port, cancellationTokenSource) { }

  public ReverseCommunicator(
    IPEndPoint ipe,
    CancellationTokenSource cancellationTokenSource = null)
    : this(ipe.Address, ipe.Port, cancellationTokenSource) { }

  public async Task<bool> CheckIfAlive()
  {
    try
    {
      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        _cancellationTokenSource.Token,
        timeoutCts.Token);

      var resJson = await SendRequestAsync("ping", cancellationToken: linkedCts.Token);
      return resJson?.Contains("pong") == true;
    }
    catch
    {
      return false;
    }
  }

  public Task InvokeCallback(
    int token,
    DateTime timestamp,
    params ObjectOrRemoteAddress[] args)
  {
    var invocReq = new CallbackInvocationRequest
    {
      Timestamp = timestamp,
      Token = token,
      Parameters = args.ToList()
    };

    var requestJsonBody = JsonConvert.SerializeObject(invocReq, s_jsonSettings);
    return SendRequestAsync("invoke_callback", null, requestJsonBody, true);
  }
}
