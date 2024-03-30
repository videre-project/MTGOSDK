﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using ScubaDiver.API.Exceptions;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.API.Interactions.Object;
using ScubaDiver.API.Utils;


namespace ScubaDiver.API
{
  /// <summary>
  /// Communicates with a diver in a remote process
  /// </summary>
  public class DiverCommunicator
  {
    readonly object _withErrors = NewtonsoftProxy.JsonSerializerSettingsWithErrors;

    private readonly string _hostname;
    public int DiverPort { get; private set; }

    private int? _process_id = null;
    private CallbacksListener _listener;
    private static readonly object _listenerLock = new();
    private readonly HttpClient httpClient = new();

    public DiverCommunicator(string hostname, int diverPort)
    {
      _hostname = hostname;
      DiverPort = diverPort;
      _listener = new CallbacksListener(this);
    }
    public DiverCommunicator(IPAddress ipa, int diverPort) : this(ipa.ToString(), diverPort) { }
    public DiverCommunicator(IPEndPoint ipe) : this(ipe.Address, ipe.Port) { }

    private string SendRequest(
      string path,
      Dictionary<string, string> queryParams = null,
      string jsonBody = null)
    {
      queryParams ??= new();

      NameValueCollection queryString = HttpUtility.ParseQueryString(string.Empty);
      foreach (KeyValuePair<string, string> kvp in queryParams)
      {
        queryString.Add(kvp.Key, kvp.Value);
      }

      string url = $"http://{_hostname}:{DiverPort}/{path}?{queryString}";
      HttpRequestMessage msg;
      if (jsonBody == null)
      {
        msg = new HttpRequestMessage(HttpMethod.Get, url);
      }
      else
      {
        msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
      }

      HttpResponseMessage res = null;
      lock (_listenerLock)
      {
        res = httpClient.SendAsync(msg).Result;
      }

      string body = res.Content.ReadAsStringAsync().Result;
      if (body.StartsWith("{\"error\":", StringComparison.InvariantCultureIgnoreCase))
      {
        // Diver sent back an error. We parse it here and throwing a 'proxied' exception
        var errMessage = JsonConvert.DeserializeObject<DiverError>(body, _withErrors);
        if (errMessage != null)
          throw new RemoteException(errMessage.Error, errMessage.StackTrace);
      }

      return body;
    }

    public bool KillDiver()
    {
      if (_process_id.HasValue)
        UnregisterClient(_process_id.Value);

      string body = SendRequest("die");
      return body?.Contains("Goodbye") ?? false;
    }

    /// <summary>
    /// Dumps the heap of the remote process
    /// </summary>
    /// <param name="typeFilter">TypeFullName filter of objects to get from the heap. Support leading/trailing wildcard (*). NULL returns all objects</param>
    /// <returns></returns>
    public HeapDump DumpHeap(string typeFilter = null, bool dumpHashcodes = true)
    {
      Dictionary<string, string> queryParams = new();
      if (typeFilter != null)
        queryParams["type_filter"] = typeFilter;
      queryParams["dump_hashcodes"] = dumpHashcodes.ToString();

      string body = SendRequest("heap", queryParams);
      HeapDump heapDump = JsonConvert.DeserializeObject<HeapDump>(body);

      return heapDump;
    }
    public DomainsDump DumpDomains()
    {
      string body = SendRequest("domains", null);
      DomainsDump? results = JsonConvert.DeserializeObject<DomainsDump>(body, _withErrors);

      return results;
    }

    public TypesDump DumpTypes(string assembly)
    {
      Dictionary<string, string> queryParams = new() {};
      queryParams["assembly"] = assembly;

      string body = SendRequest("types", queryParams);
      TypesDump? results = JsonConvert.DeserializeObject<TypesDump>(body, _withErrors);

      return results;
    }

    public TypeDump DumpType(string type, string assembly = null)
    {
      TypeDumpRequest dumpRequest = new() { TypeFullName = type };
      if (assembly != null)
        dumpRequest.Assembly = assembly;

      var requestJsonBody = JsonConvert.SerializeObject(dumpRequest);
      string body = SendRequest("type", null, requestJsonBody);
      TypeDump? results = JsonConvert.DeserializeObject<TypeDump>(body, _withErrors);

      return results;
    }

    public ObjectDump DumpObject(
      ulong address,
      string typeName,
      bool pinObject = false,
      int? hashcode = null)
    {
      Dictionary<string, string> queryParams = new()
      {
        { "address", address.ToString() },
        { "type_name", typeName },
        { "pinRequest", pinObject.ToString() },
        { "hashcode_fallback", "false" }
      };
      if (hashcode.HasValue)
      {
        queryParams["hashcode"] = hashcode.Value.ToString();
        queryParams["hashcode_fallback"] = "true";
      }

      string body = SendRequest("object", queryParams);
      if (body.Contains("\"error\":"))
      {
        if (body.Contains("'address' points at an invalid address") ||
          body.Contains("Method Table value mismatched"))
        {
          throw new RemoteObjectMovedException(address, body);
        }
        throw new Exception("Diver failed to dump objet. Error: " + body);
      }

      ObjectDump objectDump = JsonConvert.DeserializeObject<ObjectDump>(body);

      return objectDump;
    }

    public bool UnpinObject(ulong address)
    {
      Dictionary<string, string> queryParams = new()
      {
        { "address", address.ToString() },
      };
      string body = SendRequest("unpin", queryParams);
      return body.Contains("OK");
    }

    public InvocationResults InvokeMethod(
      ulong targetAddr,
      string targetTypeFullName,
      string methodName,
      string[] genericArgsFullTypeNames,
      params ObjectOrRemoteAddress[] args)
    {
      InvocationRequest invocReq = new()
      {
        ObjAddress = targetAddr,
        TypeFullName = targetTypeFullName,
        MethodName = methodName,
        GenericArgsTypeFullNames = genericArgsFullTypeNames,
        Parameters = args.ToList()
      };

      var requestJsonBody = JsonConvert.SerializeObject(invocReq);
      var resJson = SendRequest("invoke", null, requestJsonBody);

      InvocationResults res;
      try
      {
        res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);
      }
      catch
      {
        return null;
      }

      return res;
    }

    public bool RegisterClient(int? process_id = null)
    {
      _process_id = process_id ?? Process.GetCurrentProcess().Id;

      try
      {
        string body = SendRequest("register_client",
          new Dictionary<string, string> {
            { "process_id", _process_id.Value.ToString() }
          });
        return body.Contains("{\"status\":\"OK'\"}");
      }
      catch
      {
        return false;
      }
    }

    public bool UnregisterClient(int? process_id = null)
    {
      _process_id = process_id ?? Process.GetCurrentProcess().Id;

      try
      {
        string body = SendRequest("unregister_client",
          new Dictionary<string, string> {
            { "process_id", _process_id.Value.ToString() }
          });
        return body.Contains("{\"status\":\"OK'\"}");
      }
      catch
      {
        return false;
      }
      finally
      {
        _process_id = null;
      }
    }

    public bool CheckAliveness()
    {
      try
      {
        return SendRequest("ping").Contains("pong");
      }
      catch
      {
        return false;
      }
    }

    public ObjectOrRemoteAddress GetItem(ulong token, ObjectOrRemoteAddress key)
    {
      IndexedItemAccessRequest indexedItemAccess = new()
      {
        CollectionAddress = token,
        PinRequest = true,
        Index = key
      };

      var requestJsonBody = JsonConvert.SerializeObject(indexedItemAccess);
      var body = SendRequest("get_item", null, requestJsonBody);
      if (body.Contains("\"error\":"))
        throw new Exception("Diver failed to dump item of remote collection object. Error: " + body);

      InvocationResults invokeRes = JsonConvert.DeserializeObject<InvocationResults>(body);

      return invokeRes.ReturnedObjectOrAddress;
    }

    public InvocationResults InvokeStaticMethod(
      string targetTypeFullName,
      string methodName,
      params ObjectOrRemoteAddress[] args)
    => InvokeStaticMethod(targetTypeFullName, methodName, null, args);

    public InvocationResults InvokeStaticMethod(
      string targetTypeFullName,
      string methodName,
      string[] genericArgsFullTypeNames,
      params ObjectOrRemoteAddress[] args)
    => InvokeMethod(0, targetTypeFullName, methodName, genericArgsFullTypeNames, args);

    public InvocationResults CreateObject(
      string typeFullName,
      ObjectOrRemoteAddress[] args)
    {
      var ctorInvocReq = new CtorInvocationRequest()
      {
        TypeFullName = typeFullName,
        Parameters = args.ToList()
      };

      var requestJsonBody = JsonConvert.SerializeObject(ctorInvocReq);
      var resJson = SendRequest("create_object", null, requestJsonBody);
      InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);

      return res;
    }

    public InvocationResults SetField(
      ulong targetAddr,
      string targetTypeFullName,
      string fieldName,
      ObjectOrRemoteAddress newValue)
    {
      FieldSetRequest invocReq = new()
      {
        ObjAddress = targetAddr,
        TypeFullName = targetTypeFullName,
        FieldName = fieldName,
        Value = newValue
      };

      var requestJsonBody = JsonConvert.SerializeObject(invocReq);
      var resJson = SendRequest("set_field", null, requestJsonBody);
      InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);

      return res;
    }

    public InvocationResults GetField(
      ulong targetAddr,
      string targetTypeFullName,
      string fieldName)
    {
      FieldGetRequest invocReq = new()
      {
        ObjAddress = targetAddr,
        TypeFullName = targetTypeFullName,
        FieldName = fieldName,
      };

      var requestJsonBody = JsonConvert.SerializeObject(invocReq);
      var resJson = SendRequest("get_field", null, requestJsonBody);
      InvocationResults res = JsonConvert.DeserializeObject<InvocationResults>(resJson, _withErrors);

      return res;
    }

    public void EventSubscribe(
      ulong targetAddr,
      string eventName,
      LocalEventCallback callback)
    {
      if (!_listener.IsOpen)
        _listener.Open();

      Dictionary<string, string> queryParams = new()
      {
        ["address"] = targetAddr.ToString(),
        ["event"] = eventName,
        ["ip"] = _listener.IP.ToString(),
        ["port"] = _listener.Port.ToString()
      };
      string body = SendRequest("event_subscribe", queryParams);
      EventRegistrationResults regRes = JsonConvert.DeserializeObject<EventRegistrationResults>(body);
      _listener.EventSubscribe(callback, regRes.Token);
    }

    public void EventUnsubscribe(LocalEventCallback callback)
    {
      int token = _listener.EventUnsubscribe(callback);

      Dictionary<string, string> queryParams = new();
      queryParams["token"] = token.ToString();
      string body = SendRequest("event_unsubscribe", queryParams);
      if (!body.Contains("{\"status\":\"OK\"}"))
        throw new Exception("Failed to unsubscribe from an event");

      if (!_listener.HasActiveCallbacks)
        _listener.Close();
    }

    public delegate (bool voidReturnType, ObjectOrRemoteAddress res) LocalEventCallback(ObjectOrRemoteAddress[] args);
  }
}
