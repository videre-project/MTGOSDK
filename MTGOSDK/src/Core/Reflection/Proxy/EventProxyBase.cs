/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOSDK.Core.Reflection.Proxy;

[NonSerializable]
public abstract class EventProxyBase<I, T>
    : DLRWrapper<EventHandler>, IDisposable
{
  private Action? _initializer;
  private bool _isInitialized;

  public virtual string Name { get; }

  public abstract void Clear();

  public virtual void Dispose() => Try(Clear);

  public dynamic OnInitialize(Action initializer)
  {
    if (_isInitialized)
    {
      initializer();
      return this;
    }

    _initializer += initializer;
    return this;
  }

  public void DoInitialize()
  {
    if (_isInitialized)
      return;

    _initializer?.Invoke();
    _isInitialized = true;
  }

  protected void ResetInitialize() => _isInitialized = false;

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
