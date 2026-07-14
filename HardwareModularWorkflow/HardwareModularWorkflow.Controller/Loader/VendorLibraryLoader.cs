using System.Reflection;
using System.Runtime.Loader;
using HardwareModularWorkflow.Controller.Reflection;
using HardwareModularWorkflow.Controller.Common;

namespace HardwareModularWorkflow.Controller.Loader;

/// <summary>
/// 厂商库统一加载器
/// 
/// 职责：
/// 1. 扫描 VendorLibraries/ 目录下的所有子目录
/// 2. 读取每个子目录的 manifest.json
/// 3. 根据 libraryType 选择正确的加载策略（NativeDll / ManagedAssembly / EdzPackage / CompiledLibrary）
/// 4. 创建并缓存 DynamicDllInvoker / ManagedAssemblyLoader
/// 5. 提供按 VendorId 查找的能力
/// </summary>
public sealed class VendorLibraryLoader : IDisposable
{
    private readonly string _vendorLibrariesRoot;
    private readonly Dictionary<string, LoadedVendorLibrary> _loadedVendors = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    /// <summary>
    /// 创建加载器
    /// </summary>
    /// <param name="vendorLibrariesRoot">VendorLibraries 根目录绝对路径</param>
    public VendorLibraryLoader(string vendorLibrariesRoot)
    {
        _vendorLibrariesRoot = vendorLibrariesRoot ?? throw new ArgumentNullException(nameof(vendorLibrariesRoot));
        if (!Directory.Exists(_vendorLibrariesRoot))
            Directory.CreateDirectory(_vendorLibrariesRoot);
    }

    /// <summary>
    /// 扫描并加载所有厂商库
    /// </summary>
    public async Task ScanAndLoadAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var vendorDirs = Directory.GetDirectories(_vendorLibrariesRoot);
            foreach (var dir in vendorDirs)
            {
                var manifestPath = System.IO.Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue; // 跳过没有 manifest 的目录

                try
                {
                    var manifest = VendorManifest.LoadFromFile(manifestPath);
                    manifest.Validate();
                    LoadVendorLibrary(dir, manifest);
                }
                catch (Exception ex)
                {
                    // 记录但继续加载其他厂商
                    System.Diagnostics.Debug.WriteLine($"Failed to load vendor library from '{dir}': {ex.Message}");
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 加载指定厂商库
    /// </summary>
    /// <param name="vendorDirectory">厂商目录绝对路径</param>
    /// <param name="manifest">已解析的清单</param>
    public void LoadVendorLibrary(string vendorDirectory, VendorManifest manifest)
    {
        if (_loadedVendors.ContainsKey(manifest.VendorId))
            return; // 已加载

        var libType = manifest.LibraryType.ToLowerInvariant();
        IVendorLibraryDriver driver = libType switch
        {
            "nativedll" or "native" => new NativeDllDriver(vendorDirectory, manifest),
            "managedassembly" or "managed" => new ManagedAssemblyDriver(vendorDirectory, manifest),
            _ => throw new NotSupportedException(
                $"Library type '{manifest.LibraryType}' is not supported for vendor '{manifest.VendorId}'. " +
                $"Supported: NativeDll, ManagedAssembly.")
        };

        _loadedVendors[manifest.VendorId] = new LoadedVendorLibrary
        {
            VendorId = manifest.VendorId,
            VendorName = manifest.VendorName,
            ControllerType = manifest.ControllerType,
            Directory = vendorDirectory,
            Manifest = manifest,
            Driver = driver
        };
    }

    /// <summary>
    /// 获取指定厂商的驱动
    /// </summary>
    public IVendorLibraryDriver? GetDriver(string vendorId)
    {
        _lock.Wait();
        try
        {
            return _loadedVendors.TryGetValue(vendorId, out var lib) ? lib.Driver : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 获取所有已加载的厂商
    /// </summary>
    public IReadOnlyList<LoadedVendorLibrary> GetAllLoadedVendors()
    {
        _lock.Wait();
        try
        {
            return _loadedVendors.Values.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 获取指定控制器类型的所有厂商
    /// </summary>
    public IReadOnlyList<LoadedVendorLibrary> GetVendorsByControllerType(string controllerType)
    {
        return GetAllLoadedVendors()
            .Where(v => v.ControllerType.Equals(controllerType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 检查是否已加载指定厂商
    /// </summary>
    public bool IsVendorLoaded(string vendorId)
    {
        return _loadedVendors.ContainsKey(vendorId);
    }

    /// <summary>
    /// 检查是否支持指定厂商（manifest 存在且可加载）
    /// </summary>
    public bool IsVendorAvailable(string vendorId)
    {
        if (_loadedVendors.ContainsKey(vendorId))
            return true;

        var vendorDir = System.IO.Path.Combine(_vendorLibrariesRoot, vendorId);
        var manifestPath = System.IO.Path.Combine(vendorDir, "manifest.json");
        return File.Exists(manifestPath);
    }

    /// <summary>
    /// 获取默认的 VendorLibraries 根目录（相对于 Controller 项目输出目录）
    /// </summary>
    public static string GetDefaultVendorLibrariesPath()
    {
        // 尝试在多个位置查找：
        // 1. 当前执行目录 / VendorLibraries
        // 2. 当前执行目录 / ../VendorLibraries
        // 3. 当前执行目录 / ../../VendorLibraries
        var basePaths = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var basePath in basePaths.Distinct())
        {
            for (int depth = 0; depth < 4; depth++)
            {
                var path = basePath;
                for (int i = 0; i < depth; i++)
                    path = System.IO.Path.GetDirectoryName(path) ?? path;

                var vendorPath = System.IO.Path.Combine(path, "VendorLibraries");
                if (Directory.Exists(vendorPath))
                    return vendorPath;
            }
        }

        // 默认返回当前目录下的 VendorLibraries
        return System.IO.Path.Combine(AppContext.BaseDirectory, "VendorLibraries");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lock.Wait();
        try
        {
            foreach (var (_, lib) in _loadedVendors)
            {
                try
                {
                    lib.Driver.Dispose();
                }
                catch { /* ignore cleanup errors */ }
            }
            _loadedVendors.Clear();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}

/// <summary>
/// 已加载的厂商库信息
/// </summary>
public sealed class LoadedVendorLibrary
{
    public required string VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string ControllerType { get; set; } = string.Empty;
    public required string Directory { get; set; }
    public required VendorManifest Manifest { get; set; }
    public required IVendorLibraryDriver Driver { get; set; }
}

/// <summary>
/// 厂商库驱动接口（抽象 Native / Managed 差异）
/// </summary>
public interface IVendorLibraryDriver : IDisposable
{
    /// <summary>厂商 ID</summary>
    string VendorId { get; }

    /// <summary>调用 API</summary>
    object? Invoke(string apiName, params object?[] args);

    /// <summary>调用 API（泛型返回）</summary>
    T Invoke<T>(string apiName, params object?[] args);

    /// <summary>检查 API 是否可用</summary>
    bool HasApi(string apiName);

    /// <summary>获取所有可用 API</summary>
    IReadOnlyList<string> GetAvailableApiNames();
}

/// <summary>
/// Native DLL 驱动封装（基于 DynamicDllInvoker）
/// </summary>
internal sealed class NativeDllDriver : IVendorLibraryDriver
{
    private readonly DynamicDllInvoker _invoker;

    public string VendorId => _invoker.VendorId;

    public NativeDllDriver(string vendorDirectory, VendorManifest manifest)
    {
        _invoker = new DynamicDllInvoker(vendorDirectory, manifest);
        _invoker.LoadLibraries();
    }

    public object? Invoke(string apiName, params object?[] args) => _invoker.Invoke(apiName, args);
    public T Invoke<T>(string apiName, params object?[] args) => _invoker.Invoke<T>(apiName, args);
    public bool HasApi(string apiName) => _invoker.HasApi(apiName);
    public IReadOnlyList<string> GetAvailableApiNames() => _invoker.GetAvailableApiNames();

    public void Dispose() => _invoker.Dispose();
}

/// <summary>
/// Managed Assembly 驱动封装（基于 Assembly.LoadFrom + 反射）
/// </summary>
internal sealed class ManagedAssemblyDriver : IVendorLibraryDriver
{
    private readonly VendorManifest _manifest;
    private readonly string _vendorDirectory;
    private readonly List<Assembly> _assemblies = new();
    private readonly Dictionary<string, MethodInfo> _methodCache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string VendorId => _manifest.VendorId;

    public ManagedAssemblyDriver(string vendorDirectory, VendorManifest manifest)
    {
        _vendorDirectory = vendorDirectory;
        _manifest = manifest;
        LoadAssemblies();
    }

    private void LoadAssemblies()
    {
        foreach (var entry in _manifest.Libraries.Where(l => !l.Optional || File.Exists(System.IO.Path.Combine(_vendorDirectory, l.Path))))
        {
            var fullPath = System.IO.Path.Combine(_vendorDirectory, entry.Path);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                _assemblies.Add(assembly);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load assembly '{fullPath}': {ex.Message}");
            }
        }
    }

    public object? Invoke(string apiName, params object?[] args)
    {
        var method = GetOrCacheMethod(apiName);
        if (method == null)
            throw new InvalidOperationException($"Method '{apiName}' not found in any loaded assembly for vendor '{VendorId}'.");

        // 如果是静态方法直接调用
        if (method.IsStatic)
            return method.Invoke(null, args);

        // 非静态方法需要创建实例
        var instance = Activator.CreateInstance(method.DeclaringType!);
        return method.Invoke(instance, args);
    }

    public T Invoke<T>(string apiName, params object?[] args)
    {
        var result = Invoke(apiName, args);
        if (result == null) return default!;
        if (result is T typed) return typed;
        return (T)Convert.ChangeType(result, typeof(T))!;
    }

    public bool HasApi(string apiName)
    {
        return GetOrCacheMethod(apiName) != null;
    }

    public IReadOnlyList<string> GetAvailableApiNames()
    {
        return _manifest.Apis.Select(a => a.Name).ToList();
    }

    private MethodInfo? GetOrCacheMethod(string apiName)
    {
        _lock.Wait();
        try
        {
            if (_methodCache.TryGetValue(apiName, out var cached))
                return cached;

            // 从 manifest 查找对应的 API 定义
            var apiEntry = _manifest.Apis.FirstOrDefault(a => a.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (apiEntry == null) return null;

            // 在所有加载的 Assembly 中搜索类型和方法
            foreach (var assembly in _assemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    var method = type.GetMethod(apiEntry.EntryPoint,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (method != null)
                    {
                        _methodCache[apiName] = method;
                        return method;
                    }
                }
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Wait();
        try
        {
            _methodCache.Clear();
            _assemblies.Clear();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
