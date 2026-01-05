/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Concurrent;
using System.Diagnostics;

using MessagePack;

using MTGOSDK.Core.Remoting.Hooking;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


namespace MTGOSDK.Core.Remoting.Interop;

/// <summary>
/// TCP-based communicator with the Diver in a remote process.
/// </summary>
public class DiverCommunicator : IDisposable
{
  private readonly TcpCommunicator _tcp;
  private int? _process_id = null;

  // Callback handling
  private readonly ConcurrentDictionary<int, LocalEventCallback> _tokensToEventHandlers = new();
  private readonly ConcurrentDictionary<LocalEventCallback, int> _eventHandlersToToken = new();
  private readonly ConcurrentDictionary<int, LocalHookCallback> _tokensToHookCallbacks = new();
  private readonly ConcurrentDictionary<LocalHookCallback, int> _hookCallbacksToTokens = new();

  private static readonly AsyncLocal<bool> s_forceUIThread = new();
  private volatile bool _isDisposed = false;

  public static bool ForceUIThread
  {
    get => s_forceUIThread.Value;
    set => s_forceUIThread.Value = value;
  }

  public static IDisposable BeginUIThreadScope() => new UIThreadScope();

  private sealed class UIThreadScope : IDisposable
  {
    private readonly bool _previousValue;
    private readonly bool _suppressed;
    private readonly System.Threading.AsyncFlowControl _flowControl;

    public UIThreadScope()
    {
      _previousValue = ForceUIThread;
      
      // Suppress ExecutionContext flow to prevent AsyncLocal bleeding
      // to concurrent/parallel operations
      if (!System.Threading.ExecutionContext.IsFlowSuppressed())
      {
        _flowControl = System.Threading.ExecutionContext.SuppressFlow();
        _suppressed = true;
      }
      
      ForceUIThread = true;
    }

    public void Dispose()
    {
      ForceUIThread = _previousValue;
      if (_suppressed)
        _flowControl.Undo();
    }
  }

  public bool IsConnected => _tcp.IsConnected;

  public bool Disconnect()
  {
    if (!IsConnected) return false;
    return UnregisterClient(_process_id);
  }

  public void Cancel() => _tcp?.Dispose();

  public DiverCommunicator(
    string hostname,
    int diverPort,
    CancellationTokenSource cts = null)
  {
    _tcp = new TcpCommunicator(hostname, diverPort, cts);

    // Register callback handler for incoming events/hooks from Diver
    _tcp.SetCallbackHandler(HandleCallback);

    RemoteClient.Disposed += (s, e) =>
    {
      _isDisposed = true;
      SyncThread.Enqueue(() =>
      {
        Cancel();
        _process_id = null;
      });
    };
  }

  /// <summary>
  /// Connect to the Diver via TCP.
  /// </summary>
  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    await _tcp.ConnectAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Handles callbacks (events/hooks) received from the Diver.
  /// </summary>
  private void HandleCallback(string endpoint, byte[] body)
  {
    if (endpoint == "/invoke_callback")
    {
      var request = MessagePackSerializer.Deserialize<CallbackInvocationRequest>(body);

      // Set the timestamp for the sender
      if (request.Parameters.Count > 0)
        request.Parameters[0].Timestamp = request.Timestamp;

      if (_tokensToEventHandlers.TryGetValue(request.Token, out LocalEventCallback callback))
      {
        // Set timestamps for event args
        if (request.Parameters.Count > 1)
          request.Parameters[1].Timestamp = request.Timestamp;

        callback([.. request.Parameters]);
      }
      else if (_tokensToHookCallbacks.TryGetValue(request.Token, out LocalHookCallback hook))
      {
        hook(new HookContext(request.Timestamp),
             request.Parameters.FirstOrDefault(),
             [.. request.Parameters.Skip(1)]);
      }
    }
  }

  private static ReadOnlyMemory<byte> Serialize<T>(T value) =>
    MessagePackSerializer.Serialize(value);

  /// <summary>
  /// Ensures the TCP connection is established.
  /// </summary>
  private void EnsureConnected()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(DiverCommunicator), "MTGO process has closed");

    if (!_tcp.IsConnected)
    {
      _tcp.ConnectAsync().GetAwaiter().GetResult();
    }
  }

  /// <summary>
  /// Sends a request and returns the response.
  /// </summary>
  private T SendRequest<T>(string endpoint, object request = null)
  {
    EnsureConnected();
    byte[] body = request != null ? MessagePackSerializer.Serialize(request) : null;
    return _tcp.SendRequestAsync<T>("/" + endpoint, body).GetAwaiter().GetResult();
  }

  /// <summary>
  /// Sends a request without expecting a response.
  /// </summary>
  private void SendRequest(string endpoint, object request = null)
  {
    EnsureConnected();
    byte[] body = request != null ? MessagePackSerializer.Serialize(request) : null;
    _tcp.SendRequestAsync("/" + endpoint, body).GetAwaiter().GetResult();
  }

  public HeapDump DumpHeap(string typeFilter = null, bool dumpHashcodes = true)
  {
    var request = new HeapDumpRequest
    {
      TypeFilter = typeFilter,
      DumpHashcodes = dumpHashcodes
    };
    return SendRequest<HeapDump>("heap", request);
  }

  public DomainDump DumpDomain() =>
    SendRequest<DomainDump>("domains");

  public TypesDump DumpTypes(string assembly)
  {
    var request = new TypesDumpRequest { Assembly = assembly };
    return SendRequest<TypesDump>("types", request);
  }

  public TypeDump DumpType(string type, string assembly = null)
  {
    var dumpRequest = new TypeDumpRequest
    {
      TypeFullName = type,
      Assembly = assembly
    };
    return SendRequest<TypeDump>("type", dumpRequest);
  }

  public ObjectDump DumpObject(
    ulong address,
    string typeName,
    bool pinObject = false,
    int? hashcode = null)
  {
    var request = new ObjectDumpRequest
    {
      Address = address,
      TypeName = typeName,
      PinRequest = pinObject,
      Hashcode = hashcode,
      HashcodeFallback = hashcode.HasValue
    };
    return SendRequest<ObjectDump>("object", request);
  }

  public void UnpinObject(ulong address)
  {
    var request = new UnpinRequest { Address = address };
    SendRequest("unpin", request);
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
      Parameters = new List<ObjectOrRemoteAddress>(args),
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("invoke", invocReq);
  }

  public bool RegisterClient(int? process_id = null)
  {
    _process_id = process_id ?? Process.GetCurrentProcess().Id;

    try
    {
      var request = new RegisterClientRequest { ProcessId = _process_id.Value };
      SendRequest("register_client", request);
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
      var request = new UnregisterClientRequest { ProcessId = _process_id.Value };
      SendRequest("unregister_client", request);
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
      Index = key,
      ForceUIThread = ForceUIThread
    };
    var result = SendRequest<InvocationResults>("get_item", request);
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
      Parameters = new List<ObjectOrRemoteAddress>(args),
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("create_object", ctorInvocReq);
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
      Value = newValue,
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("set_field", invocReq);
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
      FieldName = fieldName,
      ForceUIThread = ForceUIThread
    };
    return SendRequest<InvocationResults>("get_field", invocReq);
  }

  public void EventSubscribe(
    ulong targetAddr,
    string eventName,
    LocalEventCallback callback)
  {
    var request = new EventSubscriptionRequest
    {
      Address = targetAddr,
      EventName = eventName
    };
    var regRes = SendRequest<EventRegistrationResults>("event_subscribe", request);
    _tokensToEventHandlers[regRes.Token] = callback;
    _eventHandlersToToken[callback] = regRes.Token;
  }

  public void EventUnsubscribe(LocalEventCallback callback)
  {
    if (!_eventHandlersToToken.TryRemove(callback, out int token))
      throw new Exception("EventUnsubscribe: callback not found");

    _tokensToEventHandlers.TryRemove(token, out _);

    var request = new EventUnsubscriptionRequest { Token = token };
    SendRequest("event_unsubscribe", request);
  }

  public bool HookMethod(
    string type,
    string methodName,
    HarmonyPatchPosition pos,
    LocalHookCallback callback,
    List<string> parametersTypeFullNames = null)
  {
    var req = new HookSubscriptionRequest
    {
      TypeFullName = type,
      MethodName = methodName,
      HookPosition = pos.ToString(),
      ParametersTypeFullNames = parametersTypeFullNames
    };

    var regRes = SendRequest<EventRegistrationResults>("hook_method", req);
    _tokensToHookCallbacks[regRes.Token] = callback;
    _hookCallbacksToTokens[callback] = regRes.Token;
    return true;
  }

  public void UnhookMethod(LocalHookCallback callback)
  {
    if (!_hookCallbacksToTokens.TryRemove(callback, out int token))
      throw new Exception("UnhookMethod: callback not found");

    _tokensToHookCallbacks.TryRemove(token, out _);

    var request = new HookUnsubscriptionRequest { Token = token };
    SendRequest("unhook_method", request);
  }

  public delegate void LocalEventCallback(ObjectOrRemoteAddress[] args);

  public void Dispose()
  {
    _tcp?.Dispose();
  }
}
