/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Diagnostics;
using System.Net;

using MessagePack;

using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// Communicates with a diver in a remote process.
/// </summary>
public class DiverCommunicator : BaseCommunicator
{
  private int? _process_id = null;
  private readonly CallbacksListener _listener;

  private static readonly AsyncLocal<bool> s_forceUIThread = new();

  public static bool ForceUIThread
  {
    get => s_forceUIThread.Value;
    set => s_forceUIThread.Value = value;
  }

  public static IDisposable BeginUIThreadScope() => new UIThreadScope();

  private sealed class UIThreadScope : IDisposable
  {
    private readonly bool _previousValue;
    public UIThreadScope()
    {
      _previousValue = ForceUIThread;
      ForceUIThread = true;
    }
    public void Dispose() => ForceUIThread = _previousValue;
  }

  public bool IsConnected => _process_id.HasValue;

  public bool Disconnect()
  {
    if (!IsConnected) return false;
    return UnregisterClient(_process_id.Value);
  }

  public DiverCommunicator(
    string hostname,
    int diverPort,
    CancellationTokenSource cts = null)
      : base(hostname, diverPort, cts)
  {
    _listener = new CallbacksListener(this);
    RemoteClient.Disposed += (s, e) =>
    {
      SyncThread.Enqueue(() =>
      {
        base.Cancel();
        _process_id = null;
        if (_listener.IsOpen)
          _listener.Close();
      });
    };
  }

  public DiverCommunicator(IPAddress ipa, int diverPort)
    : this(ipa.ToString(), diverPort, null) { }

  public DiverCommunicator(IPEndPoint ipe)
    : this(ipe.Address, ipe.Port) { }

  protected override string BuildUrl(
    string path,
    Dictionary<string, string> queryParams = null)
  {
    if (ForceUIThread)
    {
      queryParams ??= new Dictionary<string, string>();
      queryParams["ui_thread"] = "true";
    }
    return base.BuildUrl(path, queryParams);
  }

  private static ReadOnlyMemory<byte> Serialize<T>(T value) =>
    MessagePackSerializer.Serialize(value);

  public HeapDump DumpHeap(string typeFilter = null, bool dumpHashcodes = true)
  {
    var queryParams = new Dictionary<string, string>();
    if (typeFilter != null)
      queryParams["type_filter"] = typeFilter;
    queryParams["dump_hashcodes"] = dumpHashcodes.ToString();

    return SendRequest<HeapDump>("heap", queryParams);
  }

  public DomainDump DumpDomain() =>
    SendRequest<DomainDump>("domains");

  public TypesDump DumpTypes(string assembly)
  {
    var queryParams = new Dictionary<string, string>
    {
      ["assembly"] = assembly
    };
    return SendRequest<TypesDump>("types", queryParams);
  }

  public TypeDump DumpType(string type, string assembly = null)
  {
    var dumpRequest = new TypeDumpRequest { TypeFullName = type };
    if (assembly != null)
      dumpRequest.Assembly = assembly;

    return SendRequest<TypeDump>("type", null, Serialize(dumpRequest));
  }

  public ObjectDump DumpObject(
    ulong address,
    string typeName,
    bool pinObject = false,
    int? hashcode = null)
  {
    var queryParams = new Dictionary<string, string>
    {
      ["address"] = address.ToString(),
      ["type_name"] = typeName,
      ["pinRequest"] = pinObject.ToString(),
      ["hashcode_fallback"] = "false"
    };
    if (hashcode.HasValue)
    {
      queryParams["hashcode"] = hashcode.Value.ToString();
      queryParams["hashcode_fallback"] = "true";
    }

    return SendRequest<ObjectDump>("object", queryParams);
  }

  public void UnpinObject(ulong address)
  {
    var queryParams = new Dictionary<string, string>
    {
      ["address"] = address.ToString()
    };
    SendRequest("unpin", queryParams);
  }

  public InvocationResults InvokeMethod(
    ulong targetAddr,
    string targetTypeFullName,
    string methodName,
    string[] genericArgsFullTypeNames,
    params ObjectOrRemoteAddress[] args)
  {
    var invocReq = new InvocationRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      MethodName = methodName,
      GenericArgsTypeFullNames = genericArgsFullTypeNames,
      Parameters = new List<ObjectOrRemoteAddress>(args)
    };

    return SendRequest<InvocationResults>("invoke", new Dictionary<string, string>(), Serialize(invocReq));
  }

  public bool RegisterClient(int? process_id = null)
  {
    _process_id = process_id ?? Process.GetCurrentProcess().Id;

    try
    {
      var queryParams = new Dictionary<string, string>
      {
        ["process_id"] = _process_id.Value.ToString()
      };
      SendRequest("register_client", queryParams);
      return true;
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
      var queryParams = new Dictionary<string, string>
      {
        ["process_id"] = _process_id.Value.ToString()
      };
      SendRequest("unregister_client", queryParams);
      return true;
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
      SendRequest("ping");
      return true;
    }
    catch
    {
      return false;
    }
  }

  public ObjectOrRemoteAddress GetItem(ulong token, ObjectOrRemoteAddress key)
  {
    var request = new IndexedItemAccessRequest
    {
      CollectionAddress = token,
      PinRequest = true,
      Index = key
    };

    var result = SendRequest<InvocationResults>("get_item", null, Serialize(request));
    return result.ReturnedObjectOrAddress;
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
    var ctorInvocReq = new CtorInvocationRequest
    {
      TypeFullName = typeFullName,
      Parameters = new List<ObjectOrRemoteAddress>(args)
    };

    return SendRequest<InvocationResults>("create_object", new Dictionary<string, string>(), Serialize(ctorInvocReq));
  }

  public InvocationResults SetField(
    ulong targetAddr,
    string targetTypeFullName,
    string fieldName,
    ObjectOrRemoteAddress newValue)
  {
    var invocReq = new FieldSetRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      FieldName = fieldName,
      Value = newValue
    };

    return SendRequest<InvocationResults>("set_field", new Dictionary<string, string>(), Serialize(invocReq));
  }

  public InvocationResults GetField(
    ulong targetAddr,
    string targetTypeFullName,
    string fieldName)
  {
    var invocReq = new FieldGetRequest
    {
      ObjAddress = targetAddr,
      TypeFullName = targetTypeFullName,
      FieldName = fieldName
    };

    return SendRequest<InvocationResults>("get_field", new Dictionary<string, string>(), Serialize(invocReq));
  }

  public void EventSubscribe(
    ulong targetAddr,
    string eventName,
    LocalEventCallback callback)
  {
    if (!_listener.IsOpen)
      _listener.Open();

    var queryParams = new Dictionary<string, string>
    {
      ["address"] = targetAddr.ToString(),
      ["event"] = eventName,
      ["ip"] = _listener.IP.ToString(),
      ["port"] = _listener.Port.ToString()
    };

    var regRes = SendRequest<EventRegistrationResults>("event_subscribe", queryParams);
    _listener.EventSubscribe(callback, regRes.Token);
  }

  public void EventUnsubscribe(LocalEventCallback callback)
  {
    int token = _listener.EventUnsubscribe(callback);

    var queryParams = new Dictionary<string, string>
    {
      ["token"] = token.ToString()
    };
    SendRequest("event_unsubscribe", queryParams);

    if (!_listener.HasActiveCallbacks)
      _listener.Close();
  }

  public bool HookMethod(
    string type,
    string methodName,
    HarmonyPatchPosition pos,
    LocalHookCallback callback,
    List<string> parametersTypeFullNames = null)
  {
    if (!_listener.IsOpen)
      _listener.Open();

    var req = new FunctionHookRequest
    {
      IP = _listener.IP.ToString(),
      Port = _listener.Port,
      TypeFullName = type,
      MethodName = methodName,
      HookPosition = pos.ToString(),
      ParametersTypeFullNames = parametersTypeFullNames
    };

    var regRes = SendRequest<EventRegistrationResults>("hook_method", null, Serialize(req));
    _listener.HookSubscribe(callback, regRes.Token);
    return true;
  }

  public void UnhookMethod(LocalHookCallback callback)
  {
    int token = _listener.HookUnsubscribe(callback);

    var queryParams = new Dictionary<string, string>
    {
      ["token"] = token.ToString()
    };
    SendRequest("unhook_method", queryParams);

    if (!_listener.HasActiveCallbacks)
      _listener.Close();
  }

  public delegate void LocalEventCallback(ObjectOrRemoteAddress[] args);
}
