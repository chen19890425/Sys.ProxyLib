using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sys.ProxyLib.Helpers
{
    internal class Pool<T> : IDisposable
    {
        private Action<T> _reset = null;
        private Func<T, bool> _whenDropAndNew = null;
        private Func<CancellationToken, Task<T>> _factory = null;
        private ConcurrentStack<AsyncLazy<T>> _pool = null;
        private CancellationTokenSource _cancelSource = new CancellationTokenSource();

        public Pool(uint size, Func<CancellationToken, Task<T>> factory, Action<T> reset = null, Func<T, bool> whenDropAndNew = null)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _reset = reset;
            _factory = factory;
            _whenDropAndNew = whenDropAndNew;
            _pool = new ConcurrentStack<AsyncLazy<T>>();

            for (var i = 0; i < size; i++)
            {
                _pool.Push(new AsyncLazy<T>(factory));
            }
        }

        public void Dispose()
        {
            this._cancelSource.Cancel(false);

            while (_pool.TryPop(out var item))
            {
                if (item.IsCreated)
                {
                    if (item.Value.IsCompleted)
                    {
                        (item.Value.Result as IDisposable)?.Dispose();
                    }

                    item.Value.Dispose();
                }
            }
        }

        public async Task<PooledObject<T>> GetObjectAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int time = 0;
            int time_add = 100;

            while (true)
            {
                if (_pool.TryPop(out var item))
                {
                    item.EnsureValue(cancellationToken, this._cancelSource.Token);

                    var value = await item.Value;

                    if (_whenDropAndNew != null && _whenDropAndNew.Invoke(value))
                    {
                        (value as IDisposable)?.Dispose();
                        item = new AsyncLazy<T>(cancel => _factory.Invoke(cancel));
                        item.EnsureValue(cancellationToken, this._cancelSource.Token);
                        value = await item.Value;
                    }

                    return new PooledObject<T>(_pool, _reset, item, value);
                }

                time += time_add;

                await Task.Delay(time_add);

                if (timeout != null)
                {
                    if (time >= timeout.Value.TotalMilliseconds)
                    {
                        throw new TimeoutException(string.Format("Timeout to get a instance of type {0}.", typeof(T)));
                    }
                }
            }
        }
    }

    internal class PooledObject<T> : IDisposable
    {
        private int _isDisposed = 0;
        private Action<T> _reset = null;
        private AsyncLazy<T> _item = null;
        private ConcurrentStack<AsyncLazy<T>> _pool = null;

        internal PooledObject(ConcurrentStack<AsyncLazy<T>> pool, Action<T> reset, AsyncLazy<T> item, T value)
        {
            _pool = pool;
            _reset = reset;
            _item = item;
            Value = value;
        }

        public T Value { get; }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
            {
                _reset?.Invoke(Value);
                _pool.Push(_item);
            }
        }
    }

    internal class AsyncLazy<T>
    {
        private readonly object _lock = new object();
        private Func<CancellationToken, Task<T>> _factory = null;

        public AsyncLazy(Func<CancellationToken, Task<T>> factory)
        {
            this._factory = factory;
        }

        public bool IsCreated { get; private set; }

        public Task<T> Value { get; private set; }

        internal void EnsureValue(CancellationToken cancellationToken, CancellationToken disposeCancellationToken)
        {
            if (!IsCreated)
            {
                lock (_lock)
                {
                    if (!IsCreated)
                    {
                        var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposeCancellationToken);
                        Value = _factory.Invoke(cancelSource.Token);
                        IsCreated = true;
                    }
                }
            }
        }
    }
}
