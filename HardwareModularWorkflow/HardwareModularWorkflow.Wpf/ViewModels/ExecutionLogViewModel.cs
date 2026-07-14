using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 执行日志 ViewModel：日志列表、过滤查询、会话详情、错误分析、导出
/// </summary>
public partial class ExecutionLogViewModel : ObservableObject
{
    private readonly ExecutionLogService _logService;

    // --- 会话列表 ---
    [ObservableProperty]
    private ObservableCollection<ExecutionSession> _sessions = new();

    [ObservableProperty]
    private ObservableCollection<ExecutionSession> _filteredSessions = new();

    [ObservableProperty]
    private ExecutionSession? _selectedSession;

    // --- 日志详情 ---
    [ObservableProperty]
    private ObservableCollection<ExecutionLog> _sessionLogs = new();

    [ObservableProperty]
    private ObservableCollection<ExecutionLog> _filteredSessionLogs = new();

    // --- 过滤条件 ---
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private bool? _filterSuccess = null; // null = all, true = success, false = failed

    [ObservableProperty]
    private string _filterSuccessOption = "All"; // All, Success, Failed
    [ObservableProperty]
    private string _filterLogType = "All"; // All, Flow, Module, Step, Controller

    // --- 统计 ---
    [ObservableProperty]
    private int _totalSessions;

    [ObservableProperty]
    private int _totalLogs;

    [ObservableProperty]
    private int _successCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private double _successRate;

    [ObservableProperty]
    private double _avgDurationMs;

    [ObservableProperty]
    private int _cancelledCount;

    // --- 状态 ---
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // 日志类型选项
    public string[] LogTypeOptions { get; } = { "All", "Flow", "Module", "Step", "Controller" };
    public string[] SuccessFilterOptions { get; } = { "All", "Success", "Failed" };

    public ExecutionLogViewModel(ExecutionLogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    partial void OnSearchTextChanged(string value) => ApplySessionFilter();
    partial void OnDateFromChanged(DateTime? value) => ApplySessionFilter();
    partial void OnDateToChanged(DateTime? value) => ApplySessionFilter();
    partial void OnFilterSuccessOptionChanged(string value) => ApplySessionFilter();
    partial void OnFilterLogTypeChanged(string value) => ApplyLogFilter();

    partial void OnSelectedSessionChanged(ExecutionSession? value)
    {
        if (value is not null)
        {
            _ = LoadSessionLogsAsync(value);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading execution logs...";
        try
        {
            var sessions = await _logService.GetRecentSessionsAsync(200);
            Sessions = new ObservableCollection<ExecutionSession>(sessions);
            ApplySessionFilter();
            CalculateStatistics();
            StatusMessage = $"Loaded {Sessions.Count} sessions";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        DateFrom = null;
        DateTo = null;
        FilterSuccess = null;
        FilterSuccessOption = "All";
        FilterLogType = "All";
        ApplySessionFilter();
    }

    [RelayCommand]
    private async Task LoadSessionLogsAsync(ExecutionSession? session)
    {
        if (session is null)
        {
            SessionLogs.Clear();
            FilteredSessionLogs.Clear();
            return;
        }

        SelectedSession = session;
        try
        {
            var logs = await _logService.GetBySessionIdAsync(session.Id);
            SessionLogs = new ObservableCollection<ExecutionLog>(logs);
            ApplyLogFilter();
            StatusMessage = $"Loaded {logs.Count} logs for session '{session.Name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var logsToExport = FilteredSessionLogs.Count > 0 ? FilteredSessionLogs : SessionLogs;
        if (logsToExport.Count == 0)
        {
            StatusMessage = "No logs to export";
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("ExecutedAt,LogType,FlowName,ModuleName,StepName,HardwareName,CommandName,IsSuccess,DurationMs,ErrorMessage,IsCancelled");
            foreach (var log in logsToExport)
            {
                sb.AppendLine($"" +
                    $"\"{log.ExecutedAt:yyyy-MM-dd HH:mm:ss.fff}\"," +
                    $"\"{log.LogType}\"," +
                    $"\"{EscapeCsv(log.FlowName)}\"," +
                    $"\"{EscapeCsv(log.ModuleName)}\"," +
                    $"\"{EscapeCsv(log.StepName)}\"," +
                    $"\"{EscapeCsv(log.HardwareName)}\"," +
                    $"\"{EscapeCsv(log.CommandName)}\"," +
                    $"{log.IsSuccess}," +
                    $"{log.DurationMs}," +
                    $"\"{EscapeCsv(log.ErrorMessage)}\"," +
                    $"{log.IsCancelled}");
            }

            var fileName = $"execution_logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var logsToExport = FilteredSessionLogs.Count > 0 ? FilteredSessionLogs : SessionLogs;
        if (logsToExport.Count == 0)
        {
            StatusMessage = "No logs to export";
            return;
        }

        try
        {
            var data = logsToExport.Select(log => new
            {
                log.ExecutedAt,
                log.LogType,
                log.FlowName,
                log.ModuleName,
                log.StepName,
                log.HardwareName,
                log.CommandName,
                log.IsSuccess,
                log.DurationMs,
                log.ErrorMessage,
                log.ErrorCode,
                log.IsCancelled,
                log.DetailsJson
            });

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"execution_logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportSessionsCsvAsync()
    {
        if (FilteredSessions.Count == 0)
        {
            StatusMessage = "No sessions to export";
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Name,StartedAt,CompletedAt,IsSuccess,TotalDurationMs,FlowCount,ModuleCount,StepCount,SuccessStepCount,FailedStepCount,IsCancelled,CancelReason");
            foreach (var session in FilteredSessions)
            {
                sb.AppendLine($"" +
                    $"\"{EscapeCsv(session.Name)}\"," +
                    $"\"{session.StartedAt:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"{session.CompletedAt:yyyy-MM-dd HH:mm:ss}\"," +
                    $"{session.IsSuccess}," +
                    $"{session.TotalDurationMs}," +
                    $"{session.FlowCount}," +
                    $"{session.ModuleCount}," +
                    $"{session.StepCount}," +
                    $"{session.SuccessStepCount}," +
                    $"{session.FailedStepCount}," +
                    $"{session.IsCancelled}," +
                    $"\"{EscapeCsv(session.CancelReason)}\"");
            }

            var fileName = $"execution_sessions_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void ApplySessionFilter()
    {
        var query = Sessions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim().ToLowerInvariant();
            query = query.Where(s =>
                (s.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (DateFrom.HasValue)
        {
            var fromUtc = DateFrom.Value.Date.ToUniversalTime();
            query = query.Where(s => s.StartedAt >= fromUtc);
        }

        if (DateTo.HasValue)
        {
            var toUtc = DateTo.Value.Date.AddDays(1).ToUniversalTime();
            query = query.Where(s => s.StartedAt < toUtc);
        }

        if (FilterSuccessOption != "All")
        {
            bool successValue = FilterSuccessOption == "Success";
            query = query.Where(s => s.IsSuccess == successValue);
        }

        FilteredSessions = new ObservableCollection<ExecutionSession>(query.OrderByDescending(s => s.StartedAt));
        CalculateStatistics();
    }

    private void ApplyLogFilter()
    {
        var query = SessionLogs.AsEnumerable();

        if (FilterLogType != "All")
        {
            query = query.Where(l => l.LogType == FilterLogType);
        }

        FilteredSessionLogs = new ObservableCollection<ExecutionLog>(query.OrderBy(l => l.ExecutedAt));
    }

    private void CalculateStatistics()
    {
        var sessions = FilteredSessions;
        TotalSessions = sessions.Count;
        SuccessCount = sessions.Count(s => s.IsSuccess);
        FailedCount = sessions.Count(s => !s.IsSuccess);
        CancelledCount = sessions.Count(s => s.IsCancelled);
        SuccessRate = TotalSessions > 0 ? (double)SuccessCount / TotalSessions * 100 : 0;
        AvgDurationMs = sessions.Any(s => s.TotalDurationMs.HasValue)
            ? sessions.Where(s => s.TotalDurationMs.HasValue).Average(s => s.TotalDurationMs!.Value)
            : 0;

        TotalLogs = SessionLogs.Count;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\"", "\"\"");
    }
}
