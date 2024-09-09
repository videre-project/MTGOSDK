/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Windows;

using MTGOSDK.Core.Reflection;

using Shiny.Core.Interfaces;


namespace MTGOSDK.API.Interface.Windows;

/// <summary>
///
/// </summary>
/// <remarks>
/// Though this class describes a generic window object, it is only used on
/// classes extending the <see cref="Shiny.Views.BaseGenericWindow"/> class
/// that simply expose the properties and methods of the <see cref="Window"/>
/// class. This class is used to provide a more generic interface for accessing
/// basic UI elements and functionality through the WPF's dispatcher thread.
/// </remarks>
public class GenericWindow(dynamic window) : DLRWrapper<Window>
{
  /// <summary>
  /// The internal reference for the binding type for the wrapped object.
  /// </summary>
  [RuntimeInternal]
  internal override Type type => typeof(IGenericWindow);

  /// <summary>
  /// Stores an internal reference to the IGenericWindow object.
  /// </summary>
  internal override dynamic obj => Unbind(window);//Bind<IGenericWindow>(window);

  //
  // DataContext properties
  //

  private ISizable m_sizable =>
    Bind<ISizable>(window.m_sizable);

  private IResizable m_resizable =>
    Bind<IResizable>(window.m_resizable);

  private IClosableViewModel m_closable =>
    Bind<IClosableViewModel>(window.m_closable);

  private IClosableViewModel m_positionable =>
    Bind<IPositionable>(window.m_positionable);

  private void SetSize() => Unbind(@base).SetSize();

  private void SetResize() => Unbind(@base).SetResize();

  private void SetPositionTop() => Unbind(@base).SetPositionTop();

  private void SetPositionLeft() => Unbind(@base).SetPositionLeft();

  //
  // IGenericWindow wrapper properties
  //

  public double? Height
  {
    get => m_sizable.Height;
    set
    {
      Unbind(@base).Height = value;
      SetSize();
    }
  }

  public double? Width
  {
    get => m_sizable.Width;
    set
    {
      Unbind(@base).Width = value;
      SetSize();
    }
  }

  // public double MinHeight
  // {
  //   get => m_resizable.MinHeight;
  //   set
  //   {
  //     m_resizable.MinHeight = value;
  //     SetResize();
  //   }
  // }

  // public double MinWidth
  // {
  //   get => m_resizable.MinWidth;
  //   set
  //   {
  //     m_resizable.MinWidth = value;
  //     SetResize();
  //   }
  // }

  // public double MaxHeight
  // {
  //   get => m_resizable.MaxHeight;
  //   set
  //   {
  //     m_resizable.MaxHeight = value;
  //     SetResize();
  //   }
  // }

  // public double MaxWidth
  // {
  //   get => m_resizable.MaxWidth;
  //   set
  //   {
  //     m_resizable.MaxWidth = value;
  //     SetResize();
  //   }
  // }

  public dynamic Foo => window.GetType().Name;

  public dynamic IsActive => window.get_IsActive();

  public bool IsTopmost => @base.Topmost;

  // public ResizeMode ResizeMode
  // {
  //   get => Cast<ResizeMode>(Unbind(@base).ResizeMode);
  //   set => Unbind(@base).ResizeMode = (int)value;
  // }

  //
  // IGenericWindow wrapper events
  //

  /// <summary>
  /// Occurs when the window is about to close.
  /// </summary>
  public EventProxy Closed =
    new(/* IGenericWindow */ window, nameof(Closed));

  //
  // BaseGenericWindow wrapper events
  //

  /// <summary>
  /// Occurs when the window is laid out, rendered, and ready for interaction.
  /// </summary>
  public EventProxy Loaded =
    new(/* BaseGenericWindow */ window, nameof(Loaded));

  /// <summary>
  /// Occurds when the window becomes the foreground window.
  /// </summary>
  public EventProxy Activated =
    new(/* BaseGenericWindow */ window, nameof(Activated));
}
