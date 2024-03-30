﻿/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/

using System;
using System.Collections.Generic;

using MTGOSDK.Core.Remoting.Internal.Reflection;


namespace MTGOSDK.Core.Remoting.Internal.ProxiedReflection;

public class ProxiedMethodOverload
{
  public Type ReturnType { get; set; }
  public List<RemoteParameterInfo> Parameters { get; set; }
  public Func<object[], object> Proxy => (object[] arr) => GenericProxy(null, arr);
  public Func<Type[], object[], object> GenericProxy { get; set; }
  public List<string> GenericArgs { get; set; }
  public bool IsGenericMethod => GenericArgs?.Count > 0;
}
