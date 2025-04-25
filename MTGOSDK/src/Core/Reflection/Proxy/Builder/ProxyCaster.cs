/** @file
  Copyright (c) 2010, Ekon Benefits.
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS0108

using System.Collections;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using Microsoft.CSharp.RuntimeBinder;

using Dynamitey;

using ImpromptuInterface;
using ImpromptuInterface.Build;
using ImpromptuInterface.Optimization;


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public class ProxyCaster(object target, IEnumerable<Type> types) : DynamicObject
{
  private readonly List<Type> _interfaceTypes = types.ToList();

  public object Target { get; } = target;

  public TypeAssembler Maker { get; set; } = DynamicTypeBuilder.s_assembler;

  public override bool TryConvert(ConvertBinder binder, out object result)
  {
    result = null;
    if (binder.Type.IsInterface)
    {
      _interfaceTypes.Insert(0, binder.Type);
      result = TypeProxy.As(Target, _interfaceTypes.ToArray());
      return true;
    }

    if (binder.Type.IsInstanceOfType(Target))
    {
      result = Target;
    }

    return false;
  }
}
