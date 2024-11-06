/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Net.Http;

using Newtonsoft.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;

namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// The reverse communicator is used by the Diver to communicate back with its
/// controller regarding callbacks invocations
/// </summary>
public class ReverseCommunicator
{
  private readonly JsonSerializerSettings _withErrors = new()
  {
    MissingMemberHandling = MissingMemberHandling.Error,
  };

  private readonly string _hostname;
  private readonly int _port;

  public ReverseCommunicator(string hostname, int port)
  {
    _hostname = hostname;
    _port = port;
  }
  public ReverseCommunicator(IPAddress ipa, int port) : this(ipa.ToString(), port) {}
  public ReverseCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) {}

  private string SendRequest(
    string path,
    Dictionary<string, string> queryParams = null,
    string jsonBody = null)
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
        Content = new StringContent(jsonBody)
      };
    }

    HttpClient c = new();
    HttpResponseMessage res = c.SendAsync(msg).Result;
    string body = res.Content.ReadAsStringAsync().Result;

    return body;
  }

  public bool CheckIfAlive()
  {
    try
    {
      var resJson = SendRequest("ping");
      if (resJson == null)
        return false;

      return resJson.Contains("pong");
    }
    catch
    {
      return false;
    }
  }

  public InvocationResults InvokeCallback(
    int token,
    string stackTrace,
    params ObjectOrRemoteAddress[] args)
  {
    CallbackInvocationRequest invocReq = new()
    {
      StackTrace = stackTrace,
      Token = token,
      Parameters = args.ToList()
    };

    var requestJsonBody = JsonConvert.SerializeObject(invocReq);
    try
    {
      string resJson = SendRequest("invoke_callback", null, requestJsonBody);
      if(resJson.Contains("\"error\":"))
        return null;

      return JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
    }
    catch
    {
      return null;
    }
  }
}
