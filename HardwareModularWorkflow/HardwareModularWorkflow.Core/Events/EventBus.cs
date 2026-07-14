using HardwareModularWorkflow.Workflow.Events;

namespace HardwareModularWorkflow.Core.Events;

/// <summary>
/// 事件总线接口：解耦 Workflow 层事件和 UI/日志层处理
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 订阅事件
    /// </summary>
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : class;

    /// <summary>
    /// 发布事件
    /// </summary>
    Task PublishAsync(WorkflowEvent @event);

    /// <summary>
    /// 同步发布（不等待处理完成）
    /// </summary>
    void Publish(WorkflowEvent @event);
}

/// <summary>
/// 基于 Channel 的内存事件总线实现
/// 支持多个订阅者，异步处理，不会阻塞发布者
/// </summary>
public sealed class EventBus : IEventBus, IAsyncDisposable
{
    private readonly System.Threading.Channels.Channel<WorkflowEvent> _channel;
    private readonly List<(Type EventType, Delegate Handler)> _subscribers = new();
    private readonly SemaphoreSlim _subscriberLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _dispatchTask;
    private bool _started;
    private bool _disposed;

    public EventBus(int capacity = 1000)
    {
        _channel = System.Threading.Channels.Channel.CreateBounded<WorkflowEvent>(
            new System.Threading.Channels.BoundedChannelOptions(capacity) { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    }

    /// <summary>
    /// 启动事件分发循环（首次发布时自动启动）
    /// </summary>
    private void StartDispatchLoop()
    {
        if (_started) return;
        _started = true;

        _dispatchTask = Task.Run(async () =>
        {
            await foreach (var @event in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await DispatchAsync(@event);
                }
                catch
                {
                    // 事件处理异常不应中断总线
                }
            }
        }, _cts.Token);
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : class
    {
        _subscriberLock.Wait();
        try
        {
            _subscribers.Add((typeof(T), handler));
            return new Subscription(() =>
            {
                _subscriberLock.Wait();
                try
                {
                    _subscribers.RemoveAll(s => s.Handler == (Delegate)handler);
                }
                finally
                {
                    _subscriberLock.Release();
                }
            });
        }
        finally
        {
            _subscriberLock.Release();
        }
    }

    public async Task PublishAsync(WorkflowEvent @event)
    {
        if (_disposed) return;
        StartDispatchLoop();
        await _channel.Writer.WriteAsync(@event, _cts.Token);
    }

    public void Publish(WorkflowEvent @event)
    {
        if (_disposed) return;
        StartDispatchLoop();
        _channel.Writer.TryWrite(@event);
    }

    private async Task DispatchAsync(WorkflowEvent @event)
    {
        await _subscriberLock.WaitAsync(_cts.Token);
        try
        {
            var eventType = @event.GetType();
            var handlers = _subscribers
                .Where(s => s.EventType == eventType || s.EventType.IsAssignableFrom(eventType))
                .Select(s => s.Handler)
                .ToList();

            foreach (var handler in handlers)
            {
                try
                {
                    if (handler is Func<WorkflowEvent, Task> typedHandler)
                    {
                        await typedHandler(@event);
                    }
                }
                catch
                {
                    // 单个订阅者失败不影响其他订阅者
                }
            }
        }
        finally
        {
            _subscriberLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _channel.Writer.Complete();

        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { /* ignored */ }
        }

        _cts.Dispose();
        _subscriberLock.Dispose();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _unsubscribe();
        }
    }
}
