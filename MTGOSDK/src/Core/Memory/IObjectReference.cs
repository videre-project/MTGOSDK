/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Memory;

public interface IObjectReference
{
  void AddReference();
  void ReleaseReference(bool useJitter);
  bool IsValid { get; }
}
