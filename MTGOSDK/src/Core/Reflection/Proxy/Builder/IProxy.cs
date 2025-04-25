/** @file
  Copyright (c) 2010, Ekon Benefits.
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

public interface IProxy
{
  dynamic Original { get; }

  TypeAssembler Maker { get; }
}
