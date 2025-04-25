/** @file
  Copyright (c) 2010, Ekon Benefits.
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS0108


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public interface IProxyInitialize : IProxy
{
  void Initialize(
    dynamic original,
    IEnumerable<Type> interfaces = null,
    IDictionary<string, Type> informalInterface = null,
    TypeAssembler maker = null);
}
