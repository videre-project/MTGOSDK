/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;


namespace MTGOSDK.API.Collection;

public abstract class CollectionItem<T> : DLRWrapper<IMagicEntityDefinition>
{
  //
  // MagicEntityDefinition properties
  //

  public int Id => @base.Id;

  public int SourceId => @base.SourceId;

  //
  // DigitalMagicObject properties
  //

  public string Name => @base.Name;

  public string Description => @base.Description;
}
