/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net.Http;
using System.Text;
using MTGOSDK.Core.Logging;

namespace MTGOSDK.Core.Remoting.Interop;

public abstract class BaseCommunicator
{
  protected readonly string _hostname;
  protected readonly int _port;

  protected static readonly SemaphoreSlim s_semaphore =
    new(2 * Environment.ProcessorCount);
  protected static readonly HttpClient s_client = new(new HttpClientHandler
  {
    MaxConnectionsPerServer = 20
  })
  {
    Timeout = TimeSpan.FromSeconds(30),
    DefaultRequestHeaders = { ConnectionClose = false }
  };

  protected readonly CancellationTokenSource _cancellationTokenSource;

  /// <summary>
  /// Cancels all requests in progress (keeping the cancellation token source)
  /// </summary>
  public void Cancel() => _cancellationTokenSource.Cancel();

  protected BaseCommunicator(
    string hostname,
    int port,
    CancellationTokenSource cancellationTokenSource = null)
  {
    this._hostname = hostname;
    this._port = port;
    _cancellationTokenSource = cancellationTokenSource ?? new CancellationTokenSource();
  }

  private HttpRequestMessage CreateRequestMessage(
    string path,
    Dictionary<string, string> queryParams,
    string jsonBody)
  {
    var url = BuildUrl(path, queryParams);
    var msg = new HttpRequestMessage(
      jsonBody == null ? HttpMethod.Get : HttpMethod.Post,
      url);

    if (jsonBody != null)
    {
      msg.Content = new StringContent(
        jsonBody,
        Encoding.UTF8,
        "application/json");
    }

    return msg;
  }

  protected virtual async Task<string?> SendRequestAsync(
    string path,
    Dictionary<string, string> queryParams = null,
    string jsonBody = null,
    bool ignoreResponse = false,
    CancellationToken cancellationToken = default)
  {
    if (cancellationToken.IsCancellationRequested) return null;

    HttpRequestMessage request = null;
    try
    {
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _cancellationTokenSource.Token);
      timeoutCts.CancelAfter(s_client.Timeout);

      await s_semaphore.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
      request = CreateRequestMessage(path, queryParams, jsonBody);

      using var response = await s_client.SendAsync(
        request,
        ignoreResponse
          ? HttpCompletionOption.ResponseHeadersRead
          : HttpCompletionOption.ResponseContentRead,
        timeoutCts.Token).ConfigureAwait(false);

      if (ignoreResponse)
        return null;

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      Log.Warning("Request timed out or was cancelled");
      throw;
    }
    catch (HttpRequestException ex)
    {
      Log.Error($"HTTP request failed: {ex.Message}");
      Log.Debug(ex.StackTrace);
      return null;
    }
    catch (Exception ex)
    {
      if (!_cancellationTokenSource.IsCancellationRequested)
      {
        Log.Error($"Unexpected error during request: {ex.Message}");
        Log.Debug(ex.StackTrace);
      }

      return null;
    }
    finally
    {
      request?.Dispose();
      s_semaphore.Release();
    }
  }

  public string? SendRequest(
    string path,
    Dictionary<string, string> queryParams = null,
    string jsonBody = null,
    bool ignoreResponse = false)
  {
    return SendRequestAsync(path, queryParams, jsonBody, ignoreResponse)
      .GetAwaiter().GetResult();
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

  protected virtual string HandleResponse(string body)
  {
    return body;
  }

  public static void Dispose() => s_client.Dispose();
}
