/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Dynamic;


namespace MTGOSDK.Core.Reflection.Proxy;

/// <summary>
/// A dynamic proxy that intercepts property access and returns cached values.
/// Falls through to the real remote object for non-cached paths.
/// </summary>
/// <remarks>
/// This proxy is used during serialization to serve batch-fetched property
/// values, avoiding individual IPC calls for each property access.
/// </remarks>
public sealed class CachingRemoteProxy : DynamicObject
{
  private readonly dynamic _realRemote;
  private readonly Dictionary<string, object?> _cache;
  private readonly string? _pathPrefix;
  private readonly Dictionary<string, string>? _interfaceToRemotePath;

  /// <summary>
  /// Creates a new caching proxy wrapping a remote object.
  /// </summary>
  /// <param name="realRemote">The actual remote object to fall back to.</param>
  /// <param name="cache">Dictionary of remote path to cached value.</param>
  /// <param name="pathPrefix">Optional path prefix for nested object access (e.g., "PlayerEvent").</param>
  /// <param name="interfaceToRemotePath">Optional mapping from interface property names to remote paths.</param>
  public CachingRemoteProxy(
    dynamic realRemote, 
    Dictionary<string, object?> cache, 
    string? pathPrefix = null,
    Dictionary<string, string>? interfaceToRemotePath = null)
  {
    _realRemote = realRemote;
    _cache = cache ?? new Dictionary<string, object?>();
    _pathPrefix = pathPrefix;
    _interfaceToRemotePath = interfaceToRemotePath;
  }

  /// <summary>
  /// Attempts to get a member value. Returns cached value if available,
  /// otherwise falls through to real remote object.
  /// </summary>
  public override bool TryGetMember(GetMemberBinder binder, out object? result)
  {
    // Check cache first - exact match on binder name (remote path only)
    // Do NOT use interface-to-remote mapping here, as it causes conflicts:
    // e.g., CurrentRound (remote object) would map to CurrentRoundNumber (int) wrongly
    if (_cache.TryGetValue(binder.Name, out result))
    {
      // If nested dictionary, wrap in another CachingRemoteProxy
      if (result is Dictionary<string, object?> nested)
      {
        result = new CachingRemoteProxy(null!, nested, _pathPrefix, _interfaceToRemotePath);
      }
      return true;
    }

    // Check for nested path prefix (e.g., cache has "Rarity.Name", user asks for "Rarity")
    var prefix = binder.Name + ".";
    var nestedPaths = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
    if (nestedPaths.Count > 0)
    {
      // Build a nested cache for the sub-properties
      var nestedCache = new Dictionary<string, object?>();
      foreach (var path in nestedPaths)
      {
        // Remove the prefix to get the nested key
        var nestedKey = path.Substring(prefix.Length);
        nestedCache[nestedKey] = _cache[path];
      }
      
      // Also get the nested DRO for fallback (for properties not in cache)
      dynamic? nestedDro = null;
      if (_realRemote != null)
      {
        try
        {
          dynamic targetObj = _realRemote;
          if (!string.IsNullOrEmpty(_pathPrefix))
          {
            // Navigate through path prefix first
            var prefixCallSite = System.Runtime.CompilerServices.CallSite<Func<
              System.Runtime.CompilerServices.CallSite, object, object>>.Create(
              Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
                Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
                _pathPrefix,
                typeof(object),
                new[] { Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(
                  Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null) }));
            targetObj = prefixCallSite.Target(prefixCallSite, _realRemote);
          }
          // Navigate to the nested object (e.g., PlayFormat)
          var nestedCallSite = System.Runtime.CompilerServices.CallSite<Func<
            System.Runtime.CompilerServices.CallSite, object, object>>.Create(
            Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
              Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
              binder.Name,
              typeof(object),
              new[] { Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(
                Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null) }));
          nestedDro = nestedCallSite.Target(nestedCallSite, targetObj);
        }
        catch { /* Navigation failed, no fallback available */ }
      }
      
      result = new CachingRemoteProxy(nestedDro!, nestedCache);
      return true;
    }

    // Fall through to real remote if not cached
    if (_realRemote != null)
    {
      try
      {
        // Cast to object to get the real runtime type (dynamic obscures this)
        object realRemoteObj = _realRemote;
        
        // If there's a path prefix, navigate to the nested object first
        // e.g., for pathPrefix="PlayerEvent", access _realRemote.PlayerEvent.PropertyName
        dynamic targetObj = _realRemote;
        if (!string.IsNullOrEmpty(_pathPrefix))
        {
          // Navigate through the path prefix to get the nested object
          var prefixCallSite = System.Runtime.CompilerServices.CallSite<Func<
            System.Runtime.CompilerServices.CallSite, object, object>>.Create(
            Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
              Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
              _pathPrefix,
              typeof(object),
              new[] { Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(
                Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null) }));
          targetObj = prefixCallSite.Target(prefixCallSite, _realRemote);
          realRemoteObj = targetObj;
        }
        
        // If target is a DynamicObject (e.g., DynamicRemoteObject), forward TryGetMember directly
        if (realRemoteObj is DynamicObject dynObj)
        {
          return dynObj.TryGetMember(binder, out result);
        }
        
        // For other dynamic objects, use the runtime binder
        var callSite = System.Runtime.CompilerServices.CallSite<Func<
          System.Runtime.CompilerServices.CallSite, object, object>>.Create(
          Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
            Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
            binder.Name,
            typeof(object),
            new[] { Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(
              Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null) }));
        
        result = callSite.Target(callSite, targetObj);
        return true;
      }
      catch
      {
        // Property doesn't exist on remote object either
        result = null;
        return false;
      }
    }

    result = null;
    return false;
  }

  /// <summary>
  /// Checks if a path is cached.
  /// </summary>
  public bool HasCachedValue(string path) => _cache.ContainsKey(path);

  /// <summary>
  /// Gets all cached paths.
  /// </summary>
  public IEnumerable<string> CachedPaths => _cache.Keys;
}
