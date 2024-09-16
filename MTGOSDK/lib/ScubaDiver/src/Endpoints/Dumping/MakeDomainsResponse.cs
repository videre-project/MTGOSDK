/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private string MakeDomainsResponse(HttpListenerRequest req)
  {
    // Extract the names of all available modules from the current AppDomain.
    List<string> modules = new();
    string currentDomain = AppDomain.CurrentDomain.FriendlyName;
    lock (_runtime.clrLock)
    {
      ClrAppDomain clrAppDomain = _runtime.GetClrAppDomains()
        .FirstOrDefault(ad => ad.Name == currentDomain);

      modules = clrAppDomain.Modules
        .Select(m => Path.GetFileNameWithoutExtension(m.Name))
        .Where(m => !string.IsNullOrWhiteSpace(m))
        .ToList();
    }

    DomainDump domainDump = new(currentDomain, modules);
    // return JsonSerializer.Serialize(domainDump);
    // Allow parameters with null names to be serialized.
    return JsonSerializer.Serialize(domainDump, new JsonSerializerOptions {
      // IgnoreNullValues = true
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
  }
}
