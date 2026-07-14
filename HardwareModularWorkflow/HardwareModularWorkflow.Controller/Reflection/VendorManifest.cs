using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareModularWorkflow.Controller.Reflection;

/// <summary>
/// 厂商库清单（Vendor Manifest）
/// 每个厂商目录下放置一个 manifest.json，描述该厂商库的元数据、API 接口和依赖关系。
/// 系统启动时扫描并加载，用于动态反射调用。
/// </summary>
public sealed class VendorManifest
{
    /// <summary>清单格式版本</summary>
    [JsonPropertyName("manifestVersion")]
    public string ManifestVersion { get; set; } = "1.0";

    /// <summary>厂商唯一标识（如 LeadSys / ZLG / Siemens / Beckhoff）</summary>
    [JsonPropertyName("vendorId")]
    public required string VendorId { get; set; }

    /// <summary>厂商显示名称</summary>
    [JsonPropertyName("vendorName")]
    public string VendorName { get; set; } = string.Empty;

    /// <summary>控制器类型：Plc / Can / Custom</summary>
    [JsonPropertyName("controllerType")]
    public required string ControllerType { get; set; }

    /// <summary>库类型：NativeDll / ManagedAssembly / EdzPackage / CompiledLibrary</summary>
    [JsonPropertyName("libraryType")]
    public string LibraryType { get; set; } = "NativeDll";

    /// <summary>目标平台：x86 / x64 / AnyCPU</summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "x64";

    /// <summary>库文件列表（相对 manifest.json 的路径）</summary>
    [JsonPropertyName("libraries")]
    public List<VendorLibraryEntry> Libraries { get; set; } = new();

    /// <summary>API 接口清单</summary>
    [JsonPropertyName("apis")]
    public List<VendorApiEntry> Apis { get; set; } = new();

    /// <summary>依赖的其他厂商（可选）</summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    /// <summary>额外元数据（厂商自定义配置）</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; set; } = new();

    /// <summary>从 JSON 文件加载清单</summary>
    public static VendorManifest LoadFromFile(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<VendorManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidOperationException($"Failed to parse manifest: {manifestPath}");
    }

    /// <summary>验证清单完整性</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(VendorId))
            throw new InvalidOperationException("VendorManifest.VendorId is required.");
        if (string.IsNullOrWhiteSpace(ControllerType))
            throw new InvalidOperationException("VendorManifest.ControllerType is required.");
        if (Libraries.Count == 0)
            throw new InvalidOperationException($"VendorManifest '{VendorId}': at least one library entry is required.");
    }
}

/// <summary>
/// 单个库文件条目
/// </summary>
public sealed class VendorLibraryEntry
{
    /// <summary>库文件相对路径</summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>库作用：Primary / Dependency / Plugin / Resource</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "Primary";

    /// <summary>可选的描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>是否是可选依赖</summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}

/// <summary>
/// 单个 API 接口条目
/// 描述一个可被反射调用的函数/方法
/// </summary>
public sealed class VendorApiEntry
{
    /// <summary>API 逻辑名称（如 OpenDevice / SendCommand / ReadRegister）</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>在库文件中的原始函数/方法名</summary>
    [JsonPropertyName("entryPoint")]
    public required string EntryPoint { get; set; }

    /// <summary>API 类型：Function / Method / Property / Event</summary>
    [JsonPropertyName("apiType")]
    public string ApiType { get; set; } = "Function";

    /// <summary>调用约定（Native DLL）：StdCall / Cdecl / ThisCall / FastCall</summary>
    [JsonPropertyName("callingConvention")]
    public string CallingConvention { get; set; } = "StdCall";

    /// <summary>参数类型列表（C# 类型名）</summary>
    [JsonPropertyName("parameters")]
    public List<VendorParameterEntry> Parameters { get; set; } = new();

    /// <summary>返回类型（C# 类型名，如 System.Int32 / System.IntPtr / System.Void）</summary>
    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = "System.Void";

    /// <summary>参数中是否有 ref/out 修饰</summary>
    [JsonPropertyName("hasRefParameter")]
    public bool HasRefParameter { get; set; } = false;

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>构建参数类型数组</summary>
    public Type[] GetParameterTypes()
    {
        return Parameters.Select(p => ResolveType(p.Type)).ToArray();
    }

    /// <summary>构建参数名称数组</summary>
    public string[] GetParameterNames()
    {
        return Parameters.Select(p => p.Name).ToArray();
    }

    /// <summary>解析返回类型</summary>
    public Type GetReturnType()
    {
        return ResolveType(ReturnType);
    }

    private static Type ResolveType(string typeName)
    {
        // 支持常见 C# 类型别名
        typeName = typeName switch
        {
            "void" => "System.Void",
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "float" => "System.Single",
            "double" => "System.Double",
            "string" => "System.String",
            "IntPtr" => "System.IntPtr",
            "UIntPtr" => "System.UIntPtr",
            _ => typeName
        };

        return Type.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Cannot resolve type: {typeName}");
    }
}

/// <summary>
/// API 参数条目
/// </summary>
public sealed class VendorParameterEntry
{
    /// <summary>参数名</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>参数类型（C# 类型名）</summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>参数方向：In / Out / Ref</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "In";

    /// <summary>是否可选参数</summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;

    /// <summary>默认值（JSON 格式）</summary>
    [JsonPropertyName("defaultValue")]
    public JsonElement? DefaultValue { get; set; }
}
