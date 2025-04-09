/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Proxy;

[NonSerializable]
public abstract class EventProxyBase<I, T>
    : DLRWrapper<EventHandler>, IDisposable
{
  public virtual string Name { get; }

  public abstract void Clear();

  public virtual void Dispose() => Clear();

  public Delegate ProxyTypedDelegate(Delegate c) =>
    new Action<dynamic, dynamic>((dynamic obj, dynamic args) =>
    {
      switch(c.Method.GetParameters().Count())
      {
        case 2:
          c.DynamicInvoke(new dynamic[] { Cast<I>(obj), Cast<T>(args) });
          break;
        case 1:
          c.DynamicInvoke(new dynamic[] { Cast<T>(args) });
          break;
        case 0:
          c.DynamicInvoke(new dynamic[] { });
          break;
        default:
          throw new ArgumentException(
            $"Invalid number of parameters for {c.GetType().Name}.");
      }
    });
}
