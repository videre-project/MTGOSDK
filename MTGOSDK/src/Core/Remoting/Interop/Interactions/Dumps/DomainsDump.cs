/** @file
  Copyright (c) 2021, Xappy.
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0 and MIT
**/


namespace MTGOSDK.Core.Remoting.Interop.Interactions.Dumps;

public class DomainsDump
{
  public struct AvailableDomain
  {
    public string Name { get; set; }
    public List<string> AvailableModules { get; set; }
  }
  public string Current { get; set; }
  public List<AvailableDomain> AvailableDomains { get; set; }
}
