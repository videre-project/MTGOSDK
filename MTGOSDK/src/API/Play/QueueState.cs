/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.API.Play;

public enum QueueState
{
  NotSet,
  NotJoined,
  JoinRequested,
  Joined,
  RemoveRequested,
  Closed,
}
