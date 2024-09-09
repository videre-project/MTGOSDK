/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface.Windows;

public class ToastView(GenericWindow window) : GenericWindow(window)
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IToastView);

  /// <summary>
  /// Stores an internal reference to the IBaseDialog object.
  /// </summary>
  internal override dynamic obj => Bind<IToastView>(window);
}
