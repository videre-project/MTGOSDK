/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


namespace MTGOSDK.Core.Remoting.Internal.ProxiedReflection;

public class ProxiedEventInfo(RemoteObject ro, string name, List<Type> args)
    : IProxiedMember
{
  public ProxiedMemberType Type => ProxiedMemberType.Event;

  private readonly RemoteObject _ro = ro;
  private string Name { get; set; } = name;
  private List<Type> ArgumentsTypes { get; set; } = args;

  public static ProxiedEventInfo operator +(ProxiedEventInfo c1, Delegate x)
  {
    ParameterInfo[] parameters = x.Method.GetParameters();

    if (parameters.Length != c1.ArgumentsTypes.Count)
    {
      throw new Exception($"The '{c1.Name}' event expects {c1.ArgumentsTypes.Count} parameters, " +
        $"the callback that was being registered have {parameters.Length}");
    }

    if (parameters.Any(p => p.GetType().IsAssignableFrom(typeof(DynamicRemoteObject))))
    {
      throw new Exception("A Remote event's local callback must have only 'dynamic' parameters");
    }

    c1._ro.EventSubscribe(c1.Name, x);

    return c1;
  }

  public static ProxiedEventInfo operator -(ProxiedEventInfo c1, Delegate x)
  {
    c1._ro.EventUnsubscribe(c1.Name, x);
    return c1;
  }
}
