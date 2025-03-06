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

using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private ClrAppDomain _currentDomain = null;

  private string MakeDomainsResponse(HttpListenerRequest req)
  {
    // Extract the names of all available modules from the current AppDomain.
    List<string> modules = new();
    string currentDomain = AppDomain.CurrentDomain.FriendlyName;
    lock (_runtime.clrLock)
    {
      _currentDomain ??= _runtime.GetClrAppDomains()
        .FirstOrDefault(ad => ad.Name == currentDomain);

      modules = _currentDomain.Modules
        .Select(m => Path.GetFileNameWithoutExtension(m.Name))
        .Where(m => !string.IsNullOrWhiteSpace(m))
        .ToList();
    }

    DomainDump domainDump = new(currentDomain, modules);
    return JsonConvert.SerializeObject(domainDump);
  }
}
