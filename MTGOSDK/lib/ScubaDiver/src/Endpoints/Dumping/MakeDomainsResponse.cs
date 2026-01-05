/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Diagnostics.Runtime;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private ClrAppDomain _currentDomain = null;

  private byte[] MakeDomainsResponse()
  {
    string currentDomain = AppDomain.CurrentDomain.FriendlyName;
    
    // GetClrAppDomains() has internal read lock
    _currentDomain ??= _runtime.GetClrAppDomains()
      .FirstOrDefault(ad => ad.Name == currentDomain);

    var modules = _currentDomain?.Modules
      .Select(m => Path.GetFileNameWithoutExtension(m.Name))
      .Where(m => !string.IsNullOrWhiteSpace(m))
      .ToList() ?? new List<string>();

    var domainDump = new DomainDump(currentDomain, modules);
    return WrapSuccess(domainDump);
  }
}
