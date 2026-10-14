/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  /// <summary>
  /// One registration of a client process. Pins and subscriptions are counted
  /// per registration (its <see cref="Id"/>, never reused), so a process that
  /// registers again, or a reused process id, never inherits an earlier
  /// registration's references or connections. The process id is used only to
  /// tell when the client has exited.
  /// </summary>
  private sealed class RegisteredClient(int id, int pid, string key, DateTime? startTimeUtc)
  {
    public int Id { get; } = id;
    public int Pid { get; } = pid;

    /// <summary>
    /// The process id and the client's own id within it.
    /// </summary>
    public string Key { get; } = key;

    /// <summary>
    /// The process start time seen at registration (null if it could not be
    /// read), so a reused process id is never taken for this client.
    /// </summary>
    public DateTime? StartTimeUtc { get; } = startTimeUtc;

    /// <summary>
    /// Its connections that are still open.
    /// </summary>
    public HashSet<Guid> Connections { get; } = new();

    /// <summary>
    /// Its event subscriptions and hook shares (token, is a hook).
    /// </summary>
    public List<(int Token, bool Hook)> Subscriptions { get; } = new();

    public bool Removed { get; set; }
  }

  private readonly object _registeredPidsLock = new();
  private int _nextRegistrationId;

  /// <summary>
  /// The current registration of each client, by process id and the client's
  /// own id (several attachments in one process are separate clients).
  /// </summary>
  private readonly Dictionary<string, RegisteredClient> _registeredClients = new();

  /// <summary>
  /// Registrations of older clients, which send no client id: one entry per
  /// registration (several attachments in one process register the same id),
  /// with the process start time seen at registration. An older client's
  /// connections are not mapped to it and keep the previous behaviour: its
  /// pins are unattributed and its subscriptions unrecorded, so nothing it
  /// holds is reclaimed when it unregisters or exits (an older client cannot
  /// be told apart from its peers once it reconnects). Its entry only counts
  /// it as registered, and is dropped once its process has exited.
  /// </summary>
  private readonly List<(int Pid, DateTime? StartTimeUtc)> _legacyClients = new();

  private static string ClientKey(int pid, string clientId) => $"{pid}/{clientId}";

  /// <summary>
  /// The registration each connection was registered under. A closed
  /// connection stays mapped while that registration lasts (a request still
  /// running on it pins for it), and an open one stays mapped after it was
  /// removed (its later releases are refused, not taken as unattributed).
  /// </summary>
  private readonly Dictionary<Guid, RegisteredClient> _connectionClients = new();

  /// <summary>
  /// Open connections without a registration that have sent a request other
  /// than a registration (a ping counts). A current client's connection sends
  /// its registration first, so it is never among them.
  /// </summary>
  private readonly HashSet<Guid> _requestingConnections = new();

  private Timer _clientMonitor;
  private int _clientsRemoved;

  /// <summary>
  /// How often registered clients without a connection are checked for an
  /// exited process (a connection can close just before its process exits).
  /// </summary>
  private static readonly TimeSpan s_clientMonitorPeriod = TimeSpan.FromSeconds(10);

  /// <summary>
  /// The registration captured when the current request began. Later changes
  /// on the same connection cannot transfer the request's pin ownership.
  /// </summary>
  private RequestOwner CurrentRequestOwner =>
    s_requestOwner.Value ?? ResolveRequestOwner();

  private RequestOwner ResolveRequestOwner()
  {
    if (s_requestConnection.Value is not Guid connection) return RequestOwner.Unregistered;
    lock (_registeredPidsLock)
    {
      return RequestOwner.Resolve(
        _connectionClients.TryGetValue(connection, out var client) ? client.Id : null,
        _tcpServer?.IsOpen(connection) == true);
    }
  }

  /// <summary>
  /// Takes one pin reference for the current request (see <see cref="RequestOwner.Pin"/>).
  /// </summary>
  private ulong PinForRequest(object instance) => CurrentRequestOwner.Pin(_runtime, instance);


  /// <summary>
  /// Whether a connection receives callbacks, and the registration its
  /// references are counted for (null: the shared, unattributed reference).
  /// A registered client's connection receives them until the registration is
  /// removed. A connection without a registration (an older client's, or a
  /// liveness probe) receives them, as every connection did before, once it
  /// has sent a request and only while an older client is registered;
  /// otherwise nothing would ever return the shared reference.
  /// </summary>
  private (bool Receives, int? Client) CallbackRecipient(Guid connection)
  {
    lock (_registeredPidsLock)
    {
      if (_connectionClients.TryGetValue(connection, out var client))
        return client.Removed ? (false, null) : (true, client.Id);
      return (_legacyClients.Count > 0 && _requestingConnections.Contains(connection), null);
    }
  }

  private bool IsCallbackRecipient(Guid connection) => CallbackRecipient(connection).Receives;

  /// <summary>
  /// Notes a request from a connection without a registration. A registration
  /// request does not count: a current client's connection is mapped by it,
  /// and an older client's becomes a recipient with its next request.
  /// </summary>
  private void NoteRequest(Guid connection, string endpoint)
  {
    if (endpoint == "/register_client") return;
    lock (_registeredPidsLock)
    {
      if (!_connectionClients.ContainsKey(connection) && _tcpServer?.IsOpen(connection) == true)
        _requestingConnections.Add(connection);
    }
  }

  private byte[] MakeRegisterClientResponse()
  {
    var request = DeserializeRequest<RegisterClientRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int pid = request.ProcessId;
    DateTime? startTimeUtc = ReadStartTimeUtc(pid);
    if (request.ClientId == null)
    {
      lock (_registeredPidsLock) _legacyClients.Add((pid, startTimeUtc));
      Log.Debug("[Diver] New client registered (without a client id). ID = " + pid);
      return s_okResponse;
    }

    string key = ClientKey(pid, request.ClientId);
    RegisteredClient replaced = null;
    lock (_registeredPidsLock)
    {
      // The same process id from a process started at another time is a new
      // process: whatever the old one held is released first.
      if (_registeredClients.TryGetValue(key, out var existing) &&
          existing.StartTimeUtc is DateTime registered &&
          startTimeUtc is DateTime current && registered != current)
      {
        RemoveClientLocked(existing, "its process id was reused");
        replaced = existing;
      }

      if (!_registeredClients.TryGetValue(key, out var client))
      {
        client = new RegisteredClient(++_nextRegistrationId, pid, key, startTimeUtc);
        _registeredClients[key] = client;
        _runtime?.AddClient(client.Id);
      }

      // Requests over this connection now pin for this registration. A
      // connection that closed while this request was queued is not added back.
      if (s_requestConnection.Value is Guid connection)
      {
        _requestingConnections.Remove(connection);
        _connectionClients[connection] = client;
        if (_tcpServer?.IsOpen(connection) == true)
          client.Connections.Add(connection);
      }
    }
    if (replaced != null) RemoveClientSubscriptions(replaced);
    Log.Debug("[Diver] New client registered. ID = " + pid);
    return s_okResponse;
  }

  private byte[] MakeUnregisterClientResponse()
  {
    var request = DeserializeRequest<UnregisterClientRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    int pid = request.ProcessId;

    RegisteredClient removed = null;
    int remaining;
    bool wasRemoved;
    lock (_registeredPidsLock)
    {
      if (request.ClientId == null)
      {
        // An older client's registration: its pins are unattributed and stay.
        int index = _legacyClients.FindIndex(entry => entry.Pid == pid);
        wasRemoved = index >= 0;
        if (wasRemoved) _legacyClients.RemoveAt(index);
      }
      else if (_registeredClients.TryGetValue(ClientKey(pid, request.ClientId), out var client))
      {
        // A client that leaves takes its pins with it.
        RemoveClientLocked(client, "it unregistered");
        removed = client;
        wasRemoved = true;
      }
      else
      {
        wasRemoved = false;
      }
      // As before, the last client to leave releases every pin.
      remaining = _registeredClients.Count + _legacyClients.Count;
      if (remaining == 0) _runtime?.UnpinAllObjects();
    }
    if (removed != null) RemoveClientSubscriptions(removed);
    Log.Debug("[Diver] Client unregistered. ID = " + pid);

    var ucResponse = new UnregisterClientResponse
    {
      WasRemoved = wasRemoved,
      OtherClientsAmount = remaining
    };

    return WrapSuccess(ucResponse);
  }

  /// <summary>
  /// A connection closed: a registered client left with no connection whose
  /// process has exited (killed, crashed or ended without unregistering)
  /// cannot return its pins any more, so they are released.
  /// </summary>
  private void OnConnectionClosed(Guid connection)
  {
    lock (_registeredPidsLock)
    {
      _requestingConnections.Remove(connection);
      if (!_connectionClients.TryGetValue(connection, out var client))
        return;
      client.Connections.Remove(connection);
      if (client.Removed)
        _connectionClients.Remove(connection);
    }
    ReleaseExitedClients();
  }

  private void StartClientMonitor() =>
    _clientMonitor = new Timer(_ => ReleaseExitedClients(), null,
      s_clientMonitorPeriod, s_clientMonitorPeriod);

  /// <summary>
  /// Releases every registered client that has no open connection and whose
  /// process has exited (or whose process id now belongs to another process).
  /// </summary>
  private void ReleaseExitedClients()
  {
    try
    {
      List<RegisteredClient> idle;
      List<(int Pid, DateTime? StartTimeUtc)> legacy;
      lock (_registeredPidsLock)
      {
        idle = _registeredClients.Values
          .Where(client => client.Connections.Count == 0)
          .ToList();
        legacy = _legacyClients.Distinct().ToList();
      }

      // An older client's entry goes with its process; what it holds stays.
      var exitedLegacy = legacy
        .Where(entry => !IsRunning(entry.Pid, entry.StartTimeUtc))
        .ToList();
      // Process lookups run outside the lock.
      var exited = idle
        .Where(client => !IsRunning(client.Pid, client.StartTimeUtc))
        .ToList();
      if (exitedLegacy.Count == 0 && exited.Count == 0) return;

      List<RegisteredClient> removed = new();
      lock (_registeredPidsLock)
      {
        int legacyRemoved = _legacyClients.RemoveAll(exitedLegacy.Contains);
        if (legacyRemoved > 0)
          Log.Information($"[Diver] {legacyRemoved} older client registration(s) ended with their process; their pins are kept.");

        foreach (var client in exited)
        {
          // Recheck: the client may have reconnected or been replaced since.
          if (!client.Removed && client.Connections.Count == 0 &&
              _registeredClients.TryGetValue(client.Key, out var current) && current == client)
          {
            RemoveClientLocked(client, "its process has exited");
            removed.Add(client);
          }
        }

        // As when the last client unregisters: the last client to leave, by
        // exiting too, releases every pin.
        if ((legacyRemoved > 0 || removed.Count > 0) &&
            _registeredClients.Count + _legacyClients.Count == 0)
          _runtime?.UnpinAllObjects();
      }
      foreach (var client in removed)
        RemoveClientSubscriptions(client);
    }
    catch (Exception ex)
    {
      Log.Error($"[Diver] Failed to release exited clients: {ex.Message}");
    }
  }

  /// <summary>
  /// Removes a registration and releases the pin references it holds. Its
  /// event subscriptions and hook shares are removed afterwards, outside the
  /// lock (<see cref="RemoveClientSubscriptions"/>).
  /// </summary>
  private void RemoveClientLocked(RegisteredClient client, string reason)
  {
    client.Removed = true;
    if (_registeredClients.TryGetValue(client.Key, out var current) && current == client)
      _registeredClients.Remove(client.Key);

    // Closed connections are forgotten; open ones stay mapped to the removed
    // registration until they close, so their later releases are refused.
    var closed = _connectionClients
      .Where(entry => entry.Value == client && !client.Connections.Contains(entry.Key))
      .Select(entry => entry.Key)
      .ToList();
    foreach (var connection in closed)
      _connectionClients.Remove(connection);

    int released = _runtime?.RemoveClient(client.Id) ?? 0;
    Interlocked.Increment(ref _clientsRemoved);
    Log.Information($"[Diver] Client {client.Pid} removed ({reason}); {released} pin references released.");
  }

  /// <summary>
  /// The pin references of each registered client process (all its clients),
  /// for diagnostics. An older client's are unattributed and not listed.
  /// </summary>
  private Dictionary<int, int> ClientPinReferences()
  {
    List<RegisteredClient> clients;
    lock (_registeredPidsLock) clients = _registeredClients.Values.ToList();
    return clients
      .GroupBy(client => client.Pid)
      .ToDictionary(group => group.Key,
        group => group.Sum(client => _runtime?.GetClientReferenceCount(client.Id) ?? 0));
  }

  private static DateTime? ReadStartTimeUtc(int pid)
  {
    try
    {
      using var process = Process.GetProcessById(pid);
      return process.StartTime.ToUniversalTime();
    }
    catch
    {
      // Exited, or not readable from this process.
      return null;
    }
  }

  /// <summary>
  /// Whether the process registered with this id and start time still runs.
  /// A process whose start time cannot be read counts as running while its id
  /// exists; one this process cannot see at all counts as exited.
  /// </summary>
  private static bool IsRunning(int pid, DateTime? startTimeUtc)
  {
    try
    {
      using var process = Process.GetProcessById(pid);
      if (process.HasExited) return false;
      return startTimeUtc is not DateTime registered ||
        process.StartTime.ToUniversalTime() == registered;
    }
    catch (ArgumentException)
    {
      // No process with this id.
      return false;
    }
    catch
    {
      // Exists but cannot be inspected: assume it is still the client.
      return true;
    }
  }
}
