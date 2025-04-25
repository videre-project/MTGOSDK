/** @file
  Copyright (c) 2010, Ekon Benefits.
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Dynamitey.DynamicObjects;


namespace MTGOSDK.Core.Reflection.Proxy.Builder;

/// <summary>
/// Base class of Emited Proxies
/// </summary>
public abstract class Proxy : IProxyInitialize, IProxy
{
  /// <summary>
  /// Returns the proxied object
  /// </summary>
  /// <value></value>
  private dynamic ActLikeProxyOriginal { get; set; }
  private TypeAssembler ActLikeProxyActLikeMaker { get; set; }
  dynamic IProxy.Original => ActLikeProxyOriginal;
  TypeAssembler IProxy.Maker => ActLikeProxyActLikeMaker;
  private bool _init = false;

  /// <summary>
  /// Method used to Initialize Proxy
  /// </summary>
  /// <param name="original"></param>
  /// <param name="interfaces"></param>
  /// <param name="informalInterface"></param>
  /// <param name="maker"></param>
  void IProxyInitialize.Initialize(
    dynamic original,
    IEnumerable<Type> interfaces,
    IDictionary<string, Type> informalInterface,
    TypeAssembler maker)
  {
    if(((object)original) == null)
      throw new ArgumentNullException(nameof(original), "Can't proxy a Null value");

    if (_init)
      throw new MethodAccessException("Initialize should not be called twice!");
    _init = true;
    ActLikeProxyOriginal = original;

    if (maker == null)
    {
      maker = DynamicTypeBuilder.s_assembler;
    }

    ActLikeProxyActLikeMaker = maker;

    if (ActLikeProxyOriginal is IEquivalentType dynamicObj)
    {
      if (interfaces != null)
      {
        var aggreType = AggreType.MakeTypeAppendable(dynamicObj);

        foreach (var type in interfaces)
        {
          aggreType.AddType(type);
        }
      }
      if (informalInterface != null)
      {
        var aggreType = AggreType.MakeTypeAppendable(dynamicObj);
        aggreType.AddType(new PropretySpecType(informalInterface));
      }
    }
  }

  /// <summary>
  /// Determines whether the specified <see cref="System.Object"/> is equal to this instance.
  /// </summary>
  /// <param name="obj">The <see cref="System.Object"/> to compare with this instance.</param>
  /// <returns>
  /// 	<c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.
  /// </returns>
  public override bool Equals(object obj)
  {
    if (ReferenceEquals(null, obj)) return false;
    if (ReferenceEquals(this, obj)) return true;
    if (ReferenceEquals(ActLikeProxyOriginal, obj)) return true;
    if (!(obj is Proxy)) return ActLikeProxyOriginal.Equals(obj);
    return Equals((Proxy) obj);
  }

  /// <summary>
  /// Actlike proxy should be equivalent to the objects they proxy
  /// </summary>
  /// <param name="other">The other.</param>
  /// <returns></returns>
  public bool Equals(Proxy other)
  {
    if (ReferenceEquals(null, other)) return false;
    if (ReferenceEquals(this, other)) return true;
    if (ReferenceEquals(ActLikeProxyOriginal, other.ActLikeProxyOriginal)) return true;
    return Equals(other.ActLikeProxyOriginal, ActLikeProxyOriginal);
  }

  /// <summary>
  /// Returns a hash code for this instance.
  /// </summary>
  /// <returns>
  /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
  /// </returns>
  public override int GetHashCode()
  {
    return ActLikeProxyOriginal.GetHashCode();
  }

  /// <summary>
  /// Returns a <see cref="System.String"/> that represents this instance.
  /// </summary>
  /// <returns>
  /// A <see cref="System.String"/> that represents this instance.
  /// </returns>
  public override string ToString()
  {
    return ActLikeProxyOriginal.ToString();
  }
}
