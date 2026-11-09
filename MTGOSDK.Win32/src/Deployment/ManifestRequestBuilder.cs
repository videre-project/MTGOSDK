/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;


namespace MTGOSDK.Win32.Deployment;

/// <summary>
/// Requests fresh deployment manifests without changing their registered URLs.
/// </summary>
internal sealed class ManifestRequestBuilder(string manifestUrl) : IWebRequestCreate
{
  private static readonly HashSet<string> s_registeredUrls = new(StringComparer.Ordinal);

  /// <summary>
  /// Registers the manifest URL once in the current application domain.
  /// Non-HTTP URLs retain their default request handling.
  /// </summary>
  public static void Register(string url)
  {
    var uri = new Uri(url, UriKind.Absolute);
    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
      return;

    string prefix = uri.GetLeftPart(UriPartial.Path);
    lock (s_registeredUrls)
    {
      if (s_registeredUrls.Contains(prefix))
        return;

      if (!WebRequest.RegisterPrefix(prefix, new ManifestRequestBuilder(prefix)))
        throw new InvalidOperationException(
            "Could not register the deployment manifest request factory.");

      s_registeredUrls.Add(prefix);
    }
  }

  public WebRequest Create(Uri uri)
  {
    if (uri.GetLeftPart(UriPartial.Path) != manifestUrl)
      return WebRequest.CreateDefault(uri);

    // Revalidate without changing the URI. A cache-busting query makes ClickOnce
    // follow the canonical deployment provider and download the manifest again.
    var request = WebRequest.CreateDefault(uri);
    request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
    request.Headers[HttpRequestHeader.Pragma] = "no-cache";
    return request;
  }
}
