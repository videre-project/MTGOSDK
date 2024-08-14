/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

using MTGOSDK.Core.Compiler.Snapshot;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using MTGOSDK.Core.Reflection.Extensions;
using MTGOSDK.Core.Reflection.Types;
using MTGOSDK.Core.Remoting.Interop;
using MTGOSDK.Core.Remoting.Interop.Interactions;
using MTGOSDK.Core.Remoting.Interop.Interactions.Callbacks;
using MTGOSDK.Core.Remoting.Interop.Interactions.Client;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;
using MTGOSDK.Core.Remoting.Interop.Interactions.Object;


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
    return JsonConvert.SerializeObject(domainDump);
  }
}
