/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeTypeResponse()
  {
    var request = DeserializeRequest<TypeDumpRequest>();
    if (request == null)
      return QuickError("Missing or invalid request body");

    return MakeTypeResponse(request);
  }

  public byte[] MakeTypeResponse(TypeDumpRequest dumpRequest)
  {
    string type = dumpRequest.TypeFullName;
    if (string.IsNullOrEmpty(type))
      return QuickError("Missing parameter 'TypeFullName'");

    string assembly = dumpRequest.Assembly;
    Type resolvedType = _runtime.ResolveType(type, assembly);

    if (resolvedType != null)
    {
      // Log when fallback to System.Object occurs (for debugging)
      string requestedName = dumpRequest.TypeFullName;
      if (resolvedType == typeof(object) && 
          !requestedName.EndsWith("Object") && 
          !requestedName.Equals("System.Object"))
      {
         Log.Debug($"[Diver] Fallback type resolution: Requested {requestedName} resolved to System.Object");
      }

      var typeDump = TypeDump.ParseType(resolvedType);
      return WrapSuccess(typeDump);
    }

    return QuickError("Failed to find type in searched assemblies");
  }
}
