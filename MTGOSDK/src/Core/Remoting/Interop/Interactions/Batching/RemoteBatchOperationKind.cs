/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOSDK.Core.Remoting.Interop.Interactions.Batching;

public enum RemoteBatchOperationKind
{
  Read,
  Create,
  Call,
  CallStatic,
  ForEach,
  Output,
  Where,
  OrderBy,
  Count,
}
