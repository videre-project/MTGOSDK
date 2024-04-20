/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Internal.ProxiedReflection;

/// <summary>
/// Info of proxied field or property
/// </summary>
public class ProxiedValueMemberInfo(ProxiedMemberType type) : IProxiedMember
{
  public string FullTypeName { get; set; }
  public Action<object> Setter { get; set; }
  public Func<object> Getter { get; set; }

  public ProxiedMemberType Type { get; set; } = type;
}
