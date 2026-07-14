using System.Reflection;
using System.Runtime.InteropServices;
using HardwareModularWorkflow.Controller.Common;

namespace HardwareModularWorkflow.Controller.Reflection;

/// <summary>
/// 动态 DLL 反射调用器
/// 
/// 核心职责：
/// 1. 通过 NativeLibrary.Load / LoadLibrary 加载 Native DLL（无需 P/Invoke 声明）
/// 2. 通过 GetProcAddress 获取函数地址
/// 3. 使用 Marshal.GetDelegateForFunctionPointer 动态生成委托并调用
/// 
/// 优势：
/// - 无需在编译时硬编码 DllImport 声明
/// - 厂商库文件可在运行时更换/升级
/// - 支持多种库格式（.dll、.edz 解压后的 .dll、.compiled-library 等）
/// </summary>
public sealed class DynamicDllInvoker : IDisposable
{
    private readonly Dictionary<string, nint> _libraryHandles = new();
    private readonly Dictionary<string, Delegate> _cachedDelegates = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _vendorDirectory;
    private readonly VendorManifest _manifest;
    private bool _isDisposed;

    /// <summary>
    /// 创建动态调用器
    /// </summary>
    /// <param name="vendorDirectory">厂商库目录（含 manifest.json）</param>
    public DynamicDllInvoker(string vendorDirectory)
    {
        _vendorDirectory = vendorDirectory ?? throw new ArgumentNullException(nameof(vendorDirectory));
        var manifestPath = System.IO.Path.Combine(vendorDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Vendor manifest not found: {manifestPath}");

        _manifest = VendorManifest.LoadFromFile(manifestPath);
        _manifest.Validate();
    }

    /// <summary>
    /// 使用已解析的清单创建调用器
    /// </summary>
    public DynamicDllInvoker(string vendorDirectory, VendorManifest manifest)
    {
        _vendorDirectory = vendorDirectory;
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <summary>厂商 ID</summary>
    public string VendorId => _manifest.VendorId;

    /// <summary>控制器类型</summary>
    public string ControllerType => _manifest.ControllerType;

    /// <summary>加载所有声明的库文件</summary>
    public void LoadLibraries()
    {
        _lock.Wait();
        try
        {
            foreach (var entry in _manifest.Libraries)
            {
                if (entry.Optional && !IsLibraryFilePresent(entry.Path))
                    continue; // 跳过可选且缺失的库

                LoadLibrary(entry.Path);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>检查库文件是否存在</summary>
    private bool IsLibraryFilePresent(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(_vendorDirectory, relativePath);
        return File.Exists(fullPath);
    }

    /// <summary>加载单个库文件（Native DLL）</summary>
    private void LoadLibrary(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(_vendorDirectory, relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Vendor library not found: {fullPath}");

        if (_libraryHandles.ContainsKey(relativePath))
            return; // 已加载

        // 使用 NativeLibrary.Load (跨平台) 或 LoadLibrary (Windows)
        nint handle;
        try
        {
            handle = NativeLibrary.Load(fullPath);
        }
        catch (DllNotFoundException ex)
        {
            throw new ControllerException(
                $"Failed to load vendor library '{relativePath}' for vendor '{_manifest.VendorId}'. " +
                $"Ensure the DLL and all its dependencies are present in '{_vendorDirectory}'. " +
                $"Inner: {ex.Message}");
        }

        _libraryHandles[relativePath] = handle;
    }

    /// <summary>
    /// 调用指定名称的 API
    /// </summary>
    /// <param name="apiName">API 逻辑名（对应 manifest.json 中 apis[].name）</param>
    /// <param name="args">参数列表</param>
    /// <returns>函数返回值（如为 void 则返回 null）</returns>
    public object? Invoke(string apiName, params object?[] args)
    {
        var api = _manifest.Apis.FirstOrDefault(a => a.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));
        if (api == null)
            throw new InvalidOperationException($"API '{apiName}' not found in manifest for vendor '{_manifest.VendorId}'. " +
                $"Available APIs: {string.Join(", ", _manifest.Apis.Select(a => a.Name))}");

        var del = GetOrCreateDelegate(api);
        return del.DynamicInvoke(args);
    }

    /// <summary>
    /// 调用指定名称的 API（泛型返回版本）
    /// </summary>
    public T Invoke<T>(string apiName, params object?[] args)
    {
        var result = Invoke(apiName, args);
        if (result is T typedResult) return typedResult;
        if (result == null) return default!;
        return (T)Convert.ChangeType(result, typeof(T))!;
    }

    /// <summary>
    /// 获取或创建委托缓存
    /// </summary>
    private Delegate GetOrCreateDelegate(VendorApiEntry api)
    {
        _lock.Wait();
        try
        {
            var cacheKey = $"{_manifest.VendorId}:{api.Name}";
            if (_cachedDelegates.TryGetValue(cacheKey, out var cached))
                return cached;

            // 查找包含该入口点的库文件
            nint procAddress = nint.Zero;
            string? foundLibrary = null;

            foreach (var libEntry in _manifest.Libraries.Where(l => !l.Optional || IsLibraryFilePresent(l.Path)))
            {
                if (!_libraryHandles.TryGetValue(libEntry.Path, out var handle))
                {
                    LoadLibrary(libEntry.Path);
                    handle = _libraryHandles[libEntry.Path];
                }

                try
                {
                    procAddress = NativeLibrary.GetExport(handle, api.EntryPoint);
                    if (procAddress != nint.Zero)
                    {
                        foundLibrary = libEntry.Path;
                        break;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    // 当前 DLL 中未找到，继续搜索下一个
                }
            }

            if (procAddress == nint.Zero)
                throw new EntryPointNotFoundException(
                    $"API entry point '{api.EntryPoint}' not found in any library for vendor '{_manifest.VendorId}'. " +
                    $"Ensure the DLL contains this exported function.");

            // 构建委托类型并转换
            var delegateType = BuildDelegateType(api);
            var del = Marshal.GetDelegateForFunctionPointer(procAddress, delegateType);
            _cachedDelegates[cacheKey] = del;
            return del;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 根据 API 描述动态构建委托类型
    /// </summary>
    private Type BuildDelegateType(VendorApiEntry api)
    {
        var parameterTypes = api.GetParameterTypes();
        var returnType = api.GetReturnType();

        // 使用 UnmanagedCallersOnly / DllImport 风格的委托
        // 通过 Expression 或反射 emit 创建委托类型
        // 这里使用 .NET 内置的 Func<> / Action<> 组合

        if (returnType == typeof(void))
        {
            return parameterTypes.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>).MakeGenericType(parameterTypes),
                2 => typeof(Action<,>).MakeGenericType(parameterTypes),
                3 => typeof(Action<,,>).MakeGenericType(parameterTypes),
                4 => typeof(Action<,,,>).MakeGenericType(parameterTypes),
                5 => typeof(Action<,,,,>).MakeGenericType(parameterTypes),
                6 => typeof(Action<,,,,,>).MakeGenericType(parameterTypes),
                7 => typeof(Action<,,,,,,>).MakeGenericType(parameterTypes),
                8 => typeof(Action<,,,,,,,>).MakeGenericType(parameterTypes),
                9 => typeof(Action<,,,,,,,,>).MakeGenericType(parameterTypes),
                10 => typeof(Action<,,,,,,,,,>).MakeGenericType(parameterTypes),
                11 => typeof(Action<,,,,,,,,,,>).MakeGenericType(parameterTypes),
                12 => typeof(Action<,,,,,,,,,,,>).MakeGenericType(parameterTypes),
                13 => typeof(Action<,,,,,,,,,,,,>).MakeGenericType(parameterTypes),
                14 => typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(parameterTypes),
                15 => typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(parameterTypes),
                16 => typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(parameterTypes),
                _ => throw new NotSupportedException(
                    $"API '{api.Name}' has {parameterTypes.Length} parameters, but built-in Action<> only supports up to 16.")
            };
        }

        // 有返回值：使用 Func<TResult, TArgs...>
        var allTypes = new Type[parameterTypes.Length + 1];
        Array.Copy(parameterTypes, 0, allTypes, 0, parameterTypes.Length);
        allTypes[parameterTypes.Length] = returnType;

        return allTypes.Length switch
        {
            1 => typeof(Func<>).MakeGenericType(allTypes),
            2 => typeof(Func<,>).MakeGenericType(allTypes),
            3 => typeof(Func<,,>).MakeGenericType(allTypes),
            4 => typeof(Func<,,,>).MakeGenericType(allTypes),
            5 => typeof(Func<,,,,>).MakeGenericType(allTypes),
            6 => typeof(Func<,,,,,>).MakeGenericType(allTypes),
            7 => typeof(Func<,,,,,,>).MakeGenericType(allTypes),
            8 => typeof(Func<,,,,,,,>).MakeGenericType(allTypes),
            9 => typeof(Func<,,,,,,,,>).MakeGenericType(allTypes),
            10 => typeof(Func<,,,,,,,,,>).MakeGenericType(allTypes),
            11 => typeof(Func<,,,,,,,,,,>).MakeGenericType(allTypes),
            12 => typeof(Func<,,,,,,,,,,,>).MakeGenericType(allTypes),
            13 => typeof(Func<,,,,,,,,,,,,>).MakeGenericType(allTypes),
            14 => typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            15 => typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            16 => typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            17 => typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
            _ => throw new NotSupportedException(
                $"API '{api.Name}' has {parameterTypes.Length} parameters + return type, but built-in Func<> only supports up to 16 + 1.")
        };
    }

    /// <summary>检查 API 是否可用</summary>
    public bool HasApi(string apiName)
    {
        return _manifest.Apis.Any(a => a.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>获取所有可用 API 名称</summary>
    public IReadOnlyList<string> GetAvailableApiNames()
    {
        return _manifest.Apis.Select(a => a.Name).ToList();
    }

    /// <summary>释放所有加载的库</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lock.Wait();
        try
        {
            _cachedDelegates.Clear();

            foreach (var (_, handle) in _libraryHandles)
            {
                try
                {
                    NativeLibrary.Free(handle);
                }
                catch { /* ignore cleanup errors */ }
            }
            _libraryHandles.Clear();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
