/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Settings;

public interface ISetting
{
  public bool IsLoaded { get; }

  public bool IsDefault { get; }

  public bool IsReadOnly { get; }

  public bool StoreLocally { get; }

  public Setting Id { get; }
}
