/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net.Http;
using System.Net.Http.Headers;

using MessagePack;

using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions;


namespace MTGOSDK.Core.Remoting.Interop;

public abstract class BaseCommunicator
{
  protected readonly string _hostname;
  protected readonly int _port;

  // No artificial limit on concurrent requests - let the caller manage parallelism
  protected static readonly HttpClient s_client = new()
  {
    Timeout = TimeSpan.FromSeconds(30),
    DefaultRequestHeaders = { ConnectionClose = false }
  };

  private static readonly MediaTypeHeaderValue s_msgpackContentType = new("application/msgpack");

  protected readonly CancellationTokenSource _cancellationTokenSource;

  /// <summary>
  /// Cancels all requests in progress.
  /// </summary>
  public void Cancel()
  {
    try { _cancellationTokenSource.Cancel(); }
    catch (ObjectDisposedException) { }
  }

  protected BaseCommunicator(
    string hostname,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
  {
    _hostname = hostname;
    _port = port;
    _cancellationTokenSource = cancellationTokenSource ?? new CancellationTokenSource();
  }

  private HttpRequestMessage CreateBinaryRequest(
    string path,
    Dictionary<string, string> queryParams,
    ReadOnlyMemory<byte>? body)
  {
    var url = BuildUrl(path, queryParams);
    var msg = new HttpRequestMessage(
      body.HasValue ? HttpMethod.Post : HttpMethod.Get,
      url);

    if (body.HasValue)
    {
      msg.Content = new ReadOnlyMemoryContent(body.Value);
      msg.Content.Headers.ContentType = s_msgpackContentType;
    }

    return msg;
  }

  protected virtual async Task<T> SendRequestAsync<T>(
    string path,
    Dictionary<string, string> queryParams = null,
    ReadOnlyMemory<byte>? body = null,
    CancellationToken cancellationToken = default)
  {
    if (cancellationToken.IsCancellationRequested)
      return default;

    HttpRequestMessage request = null;
    try
    {
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _cancellationTokenSource.Token);
      timeoutCts.CancelAfter(s_client.Timeout);

      request = CreateBinaryRequest(path, queryParams, body);

      using var response = await s_client.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        timeoutCts.Token).ConfigureAwait(false);

      response.EnsureSuccessStatusCode();

      using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
      var wrapper = await MessagePackSerializer.DeserializeAsync<DiverResponse<T>>(stream, cancellationToken: timeoutCts.Token)
        .ConfigureAwait(false);

      if (wrapper.IsError)
      {
        throw new RemoteException(wrapper.ErrorMessage, wrapper.ErrorStackTrace);
      }

      return wrapper.Data;
    }
    catch (OperationCanceledException)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
      {
#if DEBUG
        var queryInfo = queryParams != null
          ? string.Join(", ", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"))
          : "no query parameters";
        Log.Warning($"Request to '{path}' with {queryInfo} was cancelled or timed out.");
#else
        Log.Warning($"Request to '{path}' was cancelled or timed out.");
#endif
      }
      throw;
    }
    catch (HttpRequestException ex)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
      {
        Log.Error($"HTTP request failed: {ex.Message}");
        Log.Debug(ex.StackTrace);
      }

      var uri = request?.RequestUri?.ToString() ?? $"http://{_hostname}:{_port}/{path}";
      throw new InvalidOperationException($"Request to '{uri}' failed: {ex.Message}", ex);
    }
    catch (Exception ex)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
      {
        Log.Error($"Unexpected error during request: {ex.Message}");
        Log.Debug(ex.StackTrace);
      }

      var uri = request?.RequestUri?.ToString() ?? $"http://{_hostname}:{_port}/{path}";
      throw new InvalidOperationException($"Request to '{uri}' failed: {ex.Message}", ex);
    }
    finally
    {
      request?.Dispose();
    }
  }

  protected virtual async Task SendRequestAsync(
    string path,
    Dictionary<string, string> queryParams = null,
    ReadOnlyMemory<byte>? body = null,
    CancellationToken cancellationToken = default)
  {
    if (cancellationToken.IsCancellationRequested)
      return;

    HttpRequestMessage request = null;
    try
    {
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _cancellationTokenSource.Token);
      timeoutCts.CancelAfter(s_client.Timeout);

      request = CreateBinaryRequest(path, queryParams, body);

      using var response = await s_client.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        timeoutCts.Token).ConfigureAwait(false);

      response.EnsureSuccessStatusCode();
    }
    catch (OperationCanceledException)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
        Log.Warning($"Request to '{path}' was cancelled or timed out.");
      throw;
    }
    catch (HttpRequestException ex)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
      {
        Log.Error($"HTTP request failed: {ex.Message}");
        Log.Debug(ex.StackTrace);
      }

      var uri = request?.RequestUri?.ToString() ?? $"http://{_hostname}:{_port}/{path}";
      throw new InvalidOperationException($"Request to '{uri}' failed: {ex.Message}", ex);
    }
    catch (Exception ex)
    {
      if (!_cancellationTokenSource.IsCancellationRequested && !SuppressionContext.IsSuppressed())
      {
        Log.Error($"Unexpected error during request: {ex.Message}");
        Log.Debug(ex.StackTrace);
      }

      var uri = request?.RequestUri?.ToString() ?? $"http://{_hostname}:{_port}/{path}";
      throw new InvalidOperationException($"Request to '{uri}' failed: {ex.Message}", ex);
    }
    finally
    {
      request?.Dispose();
    }
  }

  public T SendRequest<T>(
    string path,
    Dictionary<string, string> queryParams = null,
    ReadOnlyMemory<byte>? body = null)
  {
    return SendRequestAsync<T>(path, queryParams, body).GetAwaiter().GetResult();
  }

  public void SendRequest(
    string path,
    Dictionary<string, string> queryParams = null,
    ReadOnlyMemory<byte>? body = null)
  {
    SendRequestAsync(path, queryParams, body).GetAwaiter().GetResult();
  }

  protected virtual string BuildUrl(
    string path,
    Dictionary<string, string> queryParams = null)
  {
    var url = $"http://{_hostname}:{_port}/{path}";
    if (queryParams?.Count > 0)
    {
      var query = string.Join("&", queryParams.Select(kvp =>
        $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
      url += $"?{query}";
    }
    return url;
  }

  public static void Dispose() => s_client.Dispose();
}
