/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.Core.Reflection;

using WotC.MtGO.Client.Model;
using WotC.MTGO.Common;


namespace MTGOSDK.API.Collection;

public abstract class CollectionItem<T> : DLRWrapper<IMagicEntityDefinition>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  internal override Type type => typeof(T);

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
