/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeDomainsResponse()
  {
    var domain = AppDomain.CurrentDomain;
    string currentDomain = domain.FriendlyName;
    var modules = domain.GetAssemblies()
      .Select(assembly => assembly.GetName().Name)
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Distinct()
      .ToList();

    var domainDump = new DomainDump(currentDomain, modules);
    return WrapSuccess(domainDump);
  }
}
