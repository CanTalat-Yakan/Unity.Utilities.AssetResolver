using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace UnityEssentials
{
    public static class ResourceLoader
    {
        private static readonly Dictionary<string, Object> s_resourceCache = new();
        private static readonly Dictionary<string, AsyncOperationHandle> s_addressablesHandles = new();

        // In-flight preloads to avoid duplicate work.
        private static readonly Dictionary<string, Task<Object>> s_inflight = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnDomainReload()
        {
            // Domain reload / assembly reload invalidates operation handles.
            // Clearing here prevents later access to stale handles throwing
            // "Attempting to use an invalid operation handle".
            s_addressablesHandles.Clear();
            s_inflight.Clear();
            s_resourceCache.Clear();
        }

        /// <summary>
        /// Blocking, deterministic API:
        /// - Returns cached asset if available.
        /// - Otherwise loads synchronously (Addressables WaitForCompletion, then Resources fallback).
        /// - Optional: kicks off background preload when cacheResource=true and load succeeds.
        /// </summary>
        public static T TryGet<T>(
            string keyOrPath,
            bool cacheResource = false,
            bool tryResourcesFallback = true) where T : Object
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
            {
                Debug.LogWarning("ResourceLoader: keyOrPath is null or empty.");
                return null;
            }

            if (s_resourceCache.TryGetValue(keyOrPath, out var cachedObject))
            {
                if (cachedObject is T typedObject)
                    return typedObject;

                Debug.LogWarning($"ResourceLoader: Cached resource at '{keyOrPath}' is not of type {typeof(T).Name}.");
                return null;
            }

            var addressable = TryLoadAddressables<T>(keyOrPath, cacheResource);
            if (addressable != null)
                return addressable;

            if (!tryResourcesFallback)
                return null;

            var resource = Resources.Load<T>(keyOrPath);
            if (resource == null)
            {
                Debug.LogWarning($"ResourceLoader: Could not find resource '{keyOrPath}' via Addressables or Resources.");
                return null;
            }

            if (cacheResource)
                s_resourceCache[keyOrPath] = resource;

            return resource;
        }

        /// <summary>
        /// Blocking, deterministic API:
        /// - Instantiates immediately once prefab is available.
        /// - Addressables first (blocking), then Resources fallback.
        /// </summary>
        public static GameObject InstantiatePrefab(
            string keyOrPath,
            string instantiatedName = null,
            Transform parent = null,
            bool tryResourcesFallback = true)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
            {
                Debug.LogWarning("ResourceLoader: keyOrPath is null or empty.");
                return null;
            }

            // If cached prefab exists, use it.
            if (s_resourceCache.TryGetValue(keyOrPath, out var cached) && cached is GameObject cachedPrefab)
            {
                var cachedInstance = Object.Instantiate(cachedPrefab, parent);
                if (!string.IsNullOrEmpty(instantiatedName))
                    cachedInstance.name = instantiatedName;
                return cachedInstance;
            }

            var instance = TryInstantiateAddressables(keyOrPath, instantiatedName, parent);
            if (instance != null)
                return instance;

            if (!tryResourcesFallback)
                return null;

            var prefab = Resources.Load<GameObject>(keyOrPath);
            if (prefab == null)
            {
                Debug.LogWarning($"ResourceLoader: Could not find prefab '{keyOrPath}' via Addressables or Resources.");
                return null;
            }

            var resourcesInstance = Object.Instantiate(prefab, parent);
            if (!string.IsNullOrEmpty(instantiatedName))
                resourcesInstance.name = instantiatedName;

            return resourcesInstance;
        }

        /// <summary>
        /// Async performance hook:
        /// Preloads into cache in the background to reduce later stalls from TryGet/InstantiatePrefab.
        /// Safe to call repeatedly; de-duped by key.
        /// </summary>
        public static void Preload<T>(
            string keyOrPath,
            bool tryResourcesFallback = true) where T : Object
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return;

            if (s_resourceCache.ContainsKey(keyOrPath))
                return;

            if (s_inflight.TryGetValue(keyOrPath, out var existing) && !existing.IsCompleted)
                return;

            s_inflight[keyOrPath] = LoadIntoCacheAsync<T>(keyOrPath, tryResourcesFallback);
        }

        public static bool IsLoaded(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return false;

            return s_resourceCache.ContainsKey(keyOrPath);
        }

        public static bool IsLoading(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return false;

            return s_inflight.TryGetValue(keyOrPath, out var t) && !t.IsCompleted;
        }

        private static async Task<Object> LoadIntoCacheAsync<T>(
            string keyOrPath,
            bool tryResourcesFallback) where T : Object
        {
            try
            {
                // Addressables first (true async)
                var addressable = await TryLoadAddressablesAsync<T>(keyOrPath);
                if (addressable != null)
                {
                    s_resourceCache[keyOrPath] = addressable;
                    return addressable;
                }

                if (!tryResourcesFallback)
                    return null;

                // Resources fallback (sync)
                var res = Resources.Load<T>(keyOrPath);
                if (res != null)
                {
                    s_resourceCache[keyOrPath] = res;
                    return res;
                }

                return null;
            }
            finally
            {
                s_inflight.Remove(keyOrPath);
            }
        }

        private static T TryLoadAddressables<T>(string keyOrAddress, bool cacheResource) where T : Object
        {
            // If we already have a retained handle, reuse it.
            if (s_addressablesHandles.TryGetValue(keyOrAddress, out var existingHandle))
            {
                if (TryGetValidResult(existingHandle, out T typedExisting))
                {
                    if (cacheResource)
                        s_resourceCache[keyOrAddress] = typedExisting;

                    return typedExisting;
                }

                s_addressablesHandles.Remove(keyOrAddress);
                if (existingHandle.IsValid())
                {
                    try { Addressables.Release(existingHandle); } catch { }
                }
            }

            AsyncOperationHandle<T> handle;
            try { handle = Addressables.LoadAssetAsync<T>(keyOrAddress); }
            catch { return null; }

            try { handle.WaitForCompletion(); }
            catch
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            if (!TryGetValidResult<T>(handle, out var result) || result == null)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            if (cacheResource)
            {
                s_resourceCache[keyOrAddress] = result;
                // Retain handle so Release/ClearCache can free it later.
                s_addressablesHandles[keyOrAddress] = handle;
            }
            else
            {
                Addressables.Release(handle);
            }

            return result;
        }

        private static async Task<T> TryLoadAddressablesAsync<T>(string keyOrAddress) where T : Object
        {
            // If we already have a retained handle, reuse it.
            if (s_addressablesHandles.TryGetValue(keyOrAddress, out var existingHandle))
            {
                if (TryGetValidResult(existingHandle, out T typedExisting))
                    return typedExisting;

                s_addressablesHandles.Remove(keyOrAddress);
                if (existingHandle.IsValid())
                {
                    try { Addressables.Release(existingHandle); } catch { }
                }
            }

            AsyncOperationHandle<T> handle;
            try { handle = Addressables.LoadAssetAsync<T>(keyOrAddress); }
            catch { return null; }

            try { await handle.Task; }
            catch
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            if (!TryGetValidResult<T>(handle, out var result) || result == null)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            // Retain handle so ClearCache/Release can free it later.
            s_addressablesHandles[keyOrAddress] = handle;
            return result;
        }

        private static bool TryGetValidResult<T>(AsyncOperationHandle handle, out T result) where T : class
        {
            result = null;

            if (!handle.IsValid())
                return false;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                return false;

            try
            {
                result = handle.Result as T;
                return result != null;
            }
            catch
            {
                // Accessing Result can throw if the underlying internal op was released.
                result = null;
                return false;
            }
        }

        private static GameObject TryInstantiateAddressables(string keyOrAddress, string instantiatedName, Transform parent)
        {
            // Prefer InstantiateAsync for correct instance tracking.
            AsyncOperationHandle<GameObject> handle;
            try { handle = Addressables.InstantiateAsync(keyOrAddress, parent); }
            catch { return null; }

            try { handle.WaitForCompletion(); }
            catch
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return null;
            }

            var instance = handle.Result;
            if (!string.IsNullOrEmpty(instantiatedName))
                instance.name = instantiatedName;

            // Do NOT release the handle; releasing would destroy the instance.
            // Caller should later call Addressables.ReleaseInstance(instance) when done.
            return instance;
        }

        public static void Release(string keyOrPath)
        {
            if (string.IsNullOrWhiteSpace(keyOrPath))
                return;

            s_resourceCache.Remove(keyOrPath);
            s_inflight.Remove(keyOrPath);

            if (s_addressablesHandles.TryGetValue(keyOrPath, out var handle))
            {
                s_addressablesHandles.Remove(keyOrPath);
                if (handle.IsValid())
                {
                    try { Addressables.Release(handle); } catch { }
                }
            }
        }

        internal static void ClearCache()
        {
            foreach (var kvp in s_addressablesHandles)
            {
                try
                {
                    if (kvp.Value.IsValid())
                        Addressables.Release(kvp.Value);
                }
                catch { }
            }

            s_addressablesHandles.Clear();
            s_inflight.Clear();
            s_resourceCache.Clear();
        }
    }
}
