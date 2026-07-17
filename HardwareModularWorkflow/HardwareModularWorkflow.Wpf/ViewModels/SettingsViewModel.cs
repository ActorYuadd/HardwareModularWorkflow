using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Lang;
using HardwareModularWorkflow.Lang.Strings;
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
    private string _databasePath = LangKeys.Default_DatabasePath;

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
    private string _statusMessage = LangKeys.Status_Ready;

    // 语言选项
    public LanguageOption[] LanguageOptions { get; } =
    [
        new LanguageOption("zh-CN", Resources.Language_ZhCn),
        new LanguageOption("en-US", Resources.Language_EnUs)
    ];

    public SettingsViewModel()
    {
        ApplyLanguageCulture(Language);
    }

    public record LanguageOption(string Code, string DisplayName);

    partial void OnLanguageChanged(string value)
    {
        IsDirty = true;
        ApplyLanguageCulture(value);
        OnPropertyChanged(string.Empty);
    }
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
                    ApplyLanguageCulture(Language);
                }
            }
            IsDirty = false;
            StatusMessage = LangKeys.Message_SettingsLoaded;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToLoadSettings, ex.Message);
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
            StatusMessage = LangKeys.Message_SettingsSavedSuccessfully;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToSaveSettings, ex.Message);
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Language = "zh-CN";
        DarkTheme = false;
        DatabasePath = LangKeys.Default_DatabasePath;
        SchedulerMaxSlots = 50;
        LogRetentionDays = 30;
        PlcDllPath = string.Empty;
        CanDllPath = string.Empty;
        IsDirty = true;
        StatusMessage = LangKeys.Message_SettingsResetToDefaults;
    }

    private static void ApplyLanguageCulture(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            I18NExtension.Culture = culture;
            Resources.Culture = culture;
        }
        catch (CultureNotFoundException)
        {
            I18NExtension.Culture = CultureInfo.InvariantCulture;
            Resources.Culture = CultureInfo.InvariantCulture;
        }
    }

    [RelayCommand]
    private void BrowseDatabasePath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = LangKeys.DialogFilter_SqliteDatabase,
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
            Filter = LangKeys.DialogFilter_DllFiles,
            Title = LangKeys.DialogTitle_SelectPlcDll
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
            Filter = LangKeys.DialogFilter_DllFiles,
            Title = LangKeys.DialogTitle_SelectCanDll
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
        public string DatabasePath { get; set; } = LangKeys.Default_DatabasePath;
        public int SchedulerMaxSlots { get; set; } = 50;
        public int LogRetentionDays { get; set; } = 30;
        public string PlcDllPath { get; set; } = string.Empty;
        public string CanDllPath { get; set; } = string.Empty;
    }
}
