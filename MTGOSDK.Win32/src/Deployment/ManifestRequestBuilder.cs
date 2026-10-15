/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Net;


namespace MTGOSDK.Win32.Deployment;

/// <summary>
/// Bypasses cached deployment manifests for a registered URL in the current
/// application domain.
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

    string nonce = $"mtgosdkNonce={Guid.NewGuid():N}";
    var requestUri = new UriBuilder(uri)
    {
      Query = uri.Query.Length > 1
        ? $"{uri.Query.Substring(1)}&{nonce}"
        : nonce
    };

    // Use the scheme's factory rather than invoking this URL's factory again.
    return WebRequest.CreateDefault(requestUri.Uri);
  }
}
