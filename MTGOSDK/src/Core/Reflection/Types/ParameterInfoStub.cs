/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Reflection;


namespace MTGOSDK.Core.Reflection.Types;

public class ParameterInfoStub : ParameterInfo
{
  public override string Name => throw new NotImplementedException();

  public override Type ParameterType => throw new NotImplementedException();

  public override string ToString() => throw new NotImplementedException();
}
