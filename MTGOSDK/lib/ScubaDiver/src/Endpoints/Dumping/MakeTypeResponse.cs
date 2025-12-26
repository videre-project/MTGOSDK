/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;

using MessagePack;

using MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;


namespace ScubaDiver;

public partial class Diver : IDisposable
{
  private byte[] MakeTypeResponse(HttpListenerRequest req)
  {
    var body = ReadRequestBody(req);
    if (body == null || body.Length == 0)
      return QuickError("Missing body");

    TypeDumpRequest request;
    try
    {
      request = MessagePackSerializer.Deserialize<TypeDumpRequest>(body);
    }
    catch
    {
      return QuickError("Failed to deserialize body");
    }

    if (request == null)
      return QuickError("Failed to deserialize body");

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
      var typeDump = TypeDump.ParseType(resolvedType);
      return WrapSuccess(typeDump);
    }

    return QuickError("Failed to find type in searched assemblies");
  }
}
