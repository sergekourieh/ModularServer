using NLua;

namespace Server {
    public class TaskQueue {
        #region Attributes
        internal readonly Queue<Func<Task>> _taskQueue = new Queue<Func<Task>>();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly object _queueLock = new object();
        #endregion
        public async Task<T> EnqueueTask<T>(Func<Task<T>> task) {
            var tcs = new TaskCompletionSource<T>();

            lock (_queueLock) {
                _taskQueue.Enqueue(async () => {
                    try {
                        T result = await task();
                        tcs.SetResult(result);
                    } catch (Exception ex) {
                        tcs.SetException(ex);
                    }
                });
            }

            await ProcessQueue();
            return await tcs.Task;
        }
        private async Task ProcessQueue() {
            await _semaphore.WaitAsync();

            try {
                while (_taskQueue.Count > 0) {
                    Func<Task>? task = null;

                    lock (_queueLock) {
                        if (_taskQueue.Count > 0)
                            task = _taskQueue.Dequeue();
                    }

                    if (task != null) {
                        await task();
                    }
                }
            } finally {
                _semaphore.Release();
            }
        }
    }

    public class StaticTaskQueue {
        static internal Dictionary<string, Queue<Action>> luaQueues = new Dictionary<string, Queue<Action>>();//resource name, queue
        public static void initialize(string resourceName) {
            if (luaQueues.ContainsKey(resourceName)) return;
            luaQueues[resourceName] = new Queue<Action>();
            Task.Run(() => {
                ProcessQueue(resourceName);}
            );
        }
        public static void AddTaskToQueue(Action task, string resourceName) {
            if (luaQueues.ContainsKey(resourceName))
                lock (luaQueues[resourceName]) {
                    luaQueues[resourceName].Enqueue(task);
                }
            else
                Server.print("Error occured on TaskQueue, contact support.", ConsoleColor.DarkRed, false);
        }
        public static async void ProcessQueue(string resourceName) {
            while (true) {
                Action? task = null;
                if (luaQueues.TryGetValue(resourceName, out var queue)) {
                    lock (queue)
                        if (queue.Count > 0)
                            task = queue.Dequeue();
                } else {
                    break;
                }

                if (task != null)
                    task();
                else
                    await Task.Delay(200);
            }
        }

        public static void AddLuaTaskToQueue(string resourceName, string eventName, string source, LuaFunction method, params object?[] arguments) {
            AddTaskToQueue(() => {
                try {
                    Lua lua = ResourceHandler.getLua(resourceName)!;  // Get Lua instance
                    lua["eventName"] = eventName;  // Assign eventName to Lua table
                    lua["source"] = source;  // Assign source to Lua table
                    method.Call(arguments ?? Array.Empty<object>());  // Call the Lua function with arguments
                    lua["source"] = null;  // Clean up
                    lua["eventName"] = null;  // Clean up
                } catch (NLua.Exceptions.LuaScriptException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException by {resourceName} event {eventName}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException by {resourceName} event {eventName}: {luaEx.Message}", ConsoleColor.Red);
                } catch (NLua.Exceptions.LuaException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException by {resourceName} event {eventName}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException by {resourceName} event {eventName}: {luaEx.Message}", ConsoleColor.Red);
                } catch (Exception ex) {
                    Server.print($"Exception occured on {resourceName} {eventName}: {ex.Message}", ConsoleColor.Red);
                }
            }, resourceName);
        }

        public static Task<object[]> AddLuaTaskToQueueWithWait(string resourceName, string eventName, string source, LuaFunction method, params object?[] arguments) {
            var queue = new TaskQueue();
            var result = Task.FromResult(queue.EnqueueTask<object[]>(() => {
                try {
                    Lua lua = ResourceHandler.getLua(resourceName)!;  // Get Lua instance
                    lua["eventName"] = eventName;  // Assign eventName to Lua table
                    lua["source"] = source;  // Assign source to Lua table
                    object[] rtnVal = method.Call(arguments ?? Array.Empty<object>());  // Call the Lua function with arguments
                    lua["source"] = null;  // Clean up
                    lua["eventName"] = null;  // Clean up
                    return Task.FromResult<object[]>(rtnVal);
                } catch (NLua.Exceptions.LuaScriptException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException by {resourceName} event {eventName}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException by {resourceName} event {eventName}: {luaEx.Message}", ConsoleColor.Red);
                } catch (NLua.Exceptions.LuaException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException by {resourceName} event {eventName}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException by {resourceName} event {eventName}: {luaEx.Message}", ConsoleColor.Red);
                } catch (Exception ex) {
                    Server.print($"Exception occured on {resourceName} {eventName}: {ex.Message}", ConsoleColor.Red);
                }
                return Task.FromResult<object[]>(null!);
            }).GetAwaiter().GetResult());
            return result;
        }
    }
}