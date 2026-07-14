using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 系统设置 ViewModel：语言、主题、数据库、调度器、日志、DLL 路径
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private const string SettingsFileName = "app_settings.json";

    // --- 通用设置 ---
    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private bool _darkTheme = false;

    [ObservableProperty]
    private string _databasePath = "hardware_workflow.db";

    // --- 调度器设置 ---
    [ObservableProperty]
    private int _schedulerMaxSlots = 50;

    // --- 日志设置 ---
    [ObservableProperty]
    private int _logRetentionDays = 30;

    // --- DLL 路径 ---
    [ObservableProperty]
    private string _plcDllPath = string.Empty;

    [ObservableProperty]
    private string _canDllPath = string.Empty;

    // --- 状态 ---
    [ObservableProperty]
    private bool _isDirty = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // 语言选项
    public string[] LanguageOptions { get; } = { "zh-CN", "en-US" };

    public SettingsViewModel()
    {
        // 设置由 SettingsView 的 Loaded 事件触发加载
    }

    partial void OnLanguageChanged(string value) => IsDirty = true;
    partial void OnDarkThemeChanged(bool value) => IsDirty = true;
    partial void OnDatabasePathChanged(string value) => IsDirty = true;
    partial void OnSchedulerMaxSlotsChanged(int value) => IsDirty = true;
    partial void OnLogRetentionDaysChanged(int value) => IsDirty = true;
    partial void OnPlcDllPathChanged(string value) => IsDirty = true;
    partial void OnCanDllPathChanged(string value) => IsDirty = true;

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(SettingsFileName))
            {
                var json = await File.ReadAllTextAsync(SettingsFileName);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                if (settings is not null)
                {
                    Language = settings.Language;
                    DarkTheme = settings.DarkTheme;
                    DatabasePath = settings.DatabasePath;
                    SchedulerMaxSlots = settings.SchedulerMaxSlots;
                    LogRetentionDays = settings.LogRetentionDays;
                    PlcDllPath = settings.PlcDllPath;
                    CanDllPath = settings.CanDllPath;
                }
            }
            IsDirty = false;
            StatusMessage = "Settings loaded";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new SettingsData
            {
                Language = Language,
                DarkTheme = DarkTheme,
                DatabasePath = DatabasePath,
                SchedulerMaxSlots = SchedulerMaxSlots,
                LogRetentionDays = LogRetentionDays,
                PlcDllPath = PlcDllPath,
                CanDllPath = CanDllPath
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFileName, json);
            IsDirty = false;
            StatusMessage = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Language = "zh-CN";
        DarkTheme = false;
        DatabasePath = "hardware_workflow.db";
        SchedulerMaxSlots = 50;
        LogRetentionDays = 30;
        PlcDllPath = string.Empty;
        CanDllPath = string.Empty;
        IsDirty = true;
        StatusMessage = "Settings reset to defaults";
    }

    [RelayCommand]
    private void BrowseDatabasePath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
            FileName = Path.GetFileName(DatabasePath),
            InitialDirectory = string.IsNullOrEmpty(DatabasePath)
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(Path.GetFullPath(DatabasePath))
        };

        if (dialog.ShowDialog() == true)
        {
            DatabasePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowsePlcDllPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
            Title = "Select PLC DLL"
        };

        if (dialog.ShowDialog() == true)
        {
            PlcDllPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseCanDllPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
            Title = "Select CAN DLL"
        };

        if (dialog.ShowDialog() == true)
        {
            CanDllPath = dialog.FileName;
        }
    }

    /// <summary>
    /// 设置持久化数据模型
    /// </summary>
    private class SettingsData
    {
        public string Language { get; set; } = "zh-CN";
        public bool DarkTheme { get; set; } = false;
        public string DatabasePath { get; set; } = "hardware_workflow.db";
        public int SchedulerMaxSlots { get; set; } = 50;
        public int LogRetentionDays { get; set; } = 30;
        public string PlcDllPath { get; set; } = string.Empty;
        public string CanDllPath { get; set; } = string.Empty;
    }
}
