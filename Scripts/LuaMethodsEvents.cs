using NLua;
using System.Net;
using Newtonsoft.Json;
using System.Text;
using System.Data.SQLite;
using System.Text.Json;
using System.Security.Cryptography;

using Microsoft.ML;
using Microsoft.ML.Data;

namespace Server {
    public class LuaMethodsEvents {
        #region Attributes
        internal static Dictionary<string, object?> savedValues = new();
        internal static Dictionary<string, List<LuaFunction>> activeResources = new Dictionary<string, List<LuaFunction>>();
        internal static Dictionary<int, List<(string, object, string, LuaFunction)>> eventHandlers = new();//ID, List(eventName, element, resourceName, LuaFunction)
        internal static int _eventHandlerNextID = 0;
        internal static List<string> customEvents = new();
        internal static readonly string[] eventNames = {"onClientSentData", "onClientJoin", "onClientDisconnect", "onClientKicked", "onResourceStart", "onResourceRestart", "onResourceStop", "onSet", "onUDPClientConnect", "onUDPClientSentData"};
        private static readonly string[] types = {"NLua.Lua", "NLua.LuaBase", "NLua.LuaFunction", "NLua.LuaGlobalEntry", "NLua.LuaGlobals", "NLua.LuaHideAttribute", "NLua.LuaMemberAttribute", "NLua.LuaRegistrationHelper", "NLua.LuaThread", "NLua.LuaUserData", "NLua.MetaFunctions"};
        internal static Dictionary<int, List<(string, LuaFunction, string)>> luaCommandHandlers = new(); //ID, List(commandString, LuaFunction, resourceName)
        #endregion

        internal static void initiate() {
            try {
                Dictionary<object, Dictionary<string, object>> queryResult = Database.query("SELECT * FROM globalValues");
                string[]? vals = new string[2];
                int i = 0;
                foreach(var kvp in queryResult) {
                    foreach(var inner in kvp.Value) {
                        vals[i++] = (string)inner.Value;
                    }
                    i=0;
                    object? json = JsonConvert.DeserializeObject(vals[1]);
                    if (json != null)
                        savedValues.Add(vals[0], json);
                }
            } catch (Exception ex) {
                Server.print("GlobalValues load error: "+ex.Message, ConsoleColor.DarkMagenta);
            };
        }
        internal static void addToTaskQueue(string eventName, params object?[] args) {
            try {
                foreach (var kvp in eventHandlers) {
                    List<(string, object, string, LuaFunction)> list = kvp.Value;
                    foreach(var tuple in list) {
                        try {
                            if (tuple.Item1.Equals(eventName) && (tuple.Item2.Equals("root") || tuple.Item2.Equals(args[0]))) {
                                StaticTaskQueue.AddLuaTaskToQueue(tuple.Item3, tuple.Item1, (string)tuple.Item2, tuple.Item4, args);
                            }
                        } catch (NLua.Exceptions.LuaScriptException luaEx) {
                            if (luaEx.InnerException != null)
                                Server.print($"InnerException at {eventName} in {tuple.Item3}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                            else
                                Server.print($"MainException at {eventName} in {tuple.Item3}: {luaEx.Message}", ConsoleColor.Red);
                        } catch (NLua.Exceptions.LuaException luaEx) {
                            if (luaEx.InnerException != null)
                                Server.print($"InnerException at {eventName} in {tuple.Item3}: {luaEx.InnerException.Message}", ConsoleColor.Red);
                            else
                                Server.print($"MainException at {eventName} in {tuple.Item3}: {luaEx.Message}", ConsoleColor.Red);
                        }
                    }
                }
            } catch (Exception ex) {
                if (ex.InnerException != null)
                    Server.print($"InnerException at {eventName}: {ex.InnerException.Message}", ConsoleColor.DarkMagenta);
                else
                    Server.print($"MainException at {eventName}: {ex.Message}", ConsoleColor.DarkMagenta);
            }
        }
        internal static object? get(string key) {
            if (savedValues.ContainsKey(key)) {
                return savedValues[key];
            }
            return null;
        }
        internal static bool set(string key, object? value, string type) {
            if (string.IsNullOrEmpty(key))
                return false;
            if (types.Contains(type))
                return false;
            object? oldValue = null;
            string json = JsonConvert.SerializeObject(value ?? DBNull.Value);
            if (savedValues.ContainsKey(key)) {
                if (value == null) {
                    oldValue = savedValues[key];
                    savedValues.Remove(key);
                    Database.exec("DELETE FROM globalValues WHERE key=?", key);
                } else {
                    oldValue = savedValues[key];
                    Database.exec("UPDATE globalValues SET value=? WHERE key=?", json, key);
                    savedValues[key] = value;
                }
            } else {
                if (value != null) {
                    oldValue = value;
                    savedValues.Add(key, value);
                    Database.exec("INSERT INTO globalValues (key, value) VALUES (?, ?)", key, json);
                }
            }

            addToTaskQueue("onSet", key, value, oldValue);
            return true;
        }
        internal static Client[] getClients() {
            Client[] clients = new Client[Server.clients.Count];
            int i=0;
            foreach(Client client in Server.clients.Values) {
                clients[i++] = client;
            }
            return clients;
        }
        internal static int getClientsCount() {
            return Server.clients.Count;
        }
        internal static bool kickClient(Client client, string? reason = "Kicked by the server") {
            bool result = client.tcp.Disconnect(reason!).GetAwaiter().GetResult();
            if (result) {
                addToTaskQueue("onClientKicked", client, reason!);
            }
            return result;
        }
        internal static async void sendUDPToClient (IPEndPoint udpClient, string data) {
            if (Server.udpServer != null && udpClient == default(IPEndPoint) || udpClient == null) {
                Server.luaPrint("sendUDPToClient the parameter 'udpClient' is null.");
                return;
            }
            byte[] msg = Encoding.UTF8.GetBytes(data);
            _ = await Server.udpServer!.SendAsync(msg, data.Length, udpClient);
        }
        internal static async void sendTCPToClient(Client client, string json) {
            if (client == null) {
                Server.print("Error: Client parameter is null.", ConsoleColor.DarkYellow);
                return;
            } else if (string.IsNullOrEmpty(json) || string.IsNullOrWhiteSpace(json)) {
                Server.print("Error: Json argument is wrong or null", ConsoleColor.DarkYellow);
                return;
            }
            await client.tcp.sendData(json);
        }
        internal static void onClientSentData(Client client, string json) {
            if (client == null) {
                Server.print("Error: Client parameter is null.", ConsoleColor.DarkMagenta);
                return;
            } else if (string.IsNullOrEmpty(json) || string.IsNullOrWhiteSpace(json)) {
                Server.print("Error: Json argument is wrong or null", ConsoleColor.DarkMagenta);
                return;
            }
            addToTaskQueue("onClientSentData", client, json);
        }
        internal static void onClientJoin(Client client) {
            if (client == null) {
                Server.print("Error: Client parameter is null.", ConsoleColor.DarkMagenta);
                return;
            }
            addToTaskQueue("onClientJoin", client);
        }
        internal static void onClientDisconnect(Client client, string reason = "Disconnected") {
            if (client == null) {
                Server.print("Error: Client parameter is null.", ConsoleColor.DarkMagenta);
                return;
            }
            addToTaskQueue("onClientDisconnect", client, reason);
        }
        internal static void onUDPClientConnect(IPEndPoint udpClient, Client tcpClient) {
            addToTaskQueue("onUDPClientConnect", udpClient, tcpClient);
        }
        internal static void onUDPClientSentData (IPEndPoint udpClient, Client tcpClient, string data) {
            addToTaskQueue("onUDPClientSentData", udpClient, tcpClient, data);
        }
        internal static bool IsBanned(string ip) {
            return Server.bannedIPs.Contains(ip);
        }
        internal static bool BanIP(string ip) {
            return Server.bannedIPs.Add(ip);
        }
        internal static bool unBanIP(string ip) {
            if (IsBanned(ip)) {
                return Server.bannedIPs.Remove(ip);
            }
            return false;
        }
        internal static bool startResource(string resName) {
            bool result = ResourceHandler.startResource(resName);
            if (result) {
                addToTaskQueue("onResourceStart", resName);
            }
            return result;
        }
        internal static bool restartResource(string resName) {
            bool result = ResourceHandler.restartResource(resName);
            if (result) {
                addToTaskQueue("onResourceRestart", resName);
            }
            return result;
        }
        internal static bool stopResource(string resName, bool print = true) {
            if (!ResourceHandler.resources.ContainsKey(resName)) {
                Server.print($"Resource '{resName}' not found.");
                return false;
            } else if (ResourceHandler.recsState[resName] == ResourceHandler.resState.loaded
                    || ResourceHandler.recsState[resName] == ResourceHandler.resState.failedToLoad 
                    || ResourceHandler.recsState[resName] == ResourceHandler.resState.failedToRun) {
                if (print)
                    Server.print($"Resource '{resName}' is not running.");
                return false;
            }

            foreach (var kvp in eventHandlers) {
                List<(string, object, string, LuaFunction)> list = kvp.Value;
                foreach(var tuple in list) {
                    if (tuple.Item3.Equals(resName)) {
                        if (tuple.Item1.Equals("onResourceStop") && (tuple.Item2.Equals("root") || tuple.Item2.Equals(resName))) {
                            StaticTaskQueue.AddLuaTaskToQueueWithWait(tuple.Item3, tuple.Item1, (string)tuple.Item2, tuple.Item4, resName).GetAwaiter().GetResult();
                        }
                    }
                }
            }

            var queue = new TaskQueue();
            bool result = queue.EnqueueTask(() => {
                return Task.FromResult(ResourceHandler.stopResource(resName, print));
            }).GetAwaiter().GetResult();

            if (result) {
                try {
                    foreach (var kvp in eventHandlers) {
                        List<(string, object, string, LuaFunction)> list = kvp.Value;
                        foreach(var tuple in list) {
                            if (tuple.Item3.Equals(resName)) {
                                eventHandlers.Remove(kvp.Key);
                                continue;
                            }
                            if (tuple.Item1.Equals("onResourceStop") && (tuple.Item2.Equals("root") || tuple.Item2.Equals(resName))) {
                                StaticTaskQueue.AddLuaTaskToQueue(tuple.Item3, tuple.Item1, (string)tuple.Item2, tuple.Item4, resName);
                            }
                        }
                    }
                    foreach (var kvp in luaCommandHandlers) {
                        List<(string, LuaFunction, string)> list = kvp.Value;
                        foreach (var tuple in list) {
                            if (tuple.Item3.Equals(resName)) {
                                luaCommandHandlers.Remove(kvp.Key);
                            }
                        }
                    }
                } catch (NLua.Exceptions.LuaScriptException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException at stopResource: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException at stopResource: {luaEx.Message}", ConsoleColor.Red);
                } catch (NLua.Exceptions.LuaException luaEx) {
                    if (luaEx.InnerException != null)
                        Server.print($"InnerException at stopResource: {luaEx.InnerException.Message}", ConsoleColor.Red);
                    else
                        Server.print($"MainException at stopResource: {luaEx.Message}", ConsoleColor.Red);
                } catch (Exception ex) {
                    if (ex.InnerException != null)
                        Server.print($"InnerException at stopResource: {ex.InnerException.Message}", ConsoleColor.DarkMagenta);
                    else
                        Server.print($"MainException at stopResource: {ex.Message}", ConsoleColor.DarkMagenta);
                }
            }
            return result;
        }
        internal static bool stopAllResources() {
            foreach (string resourceName in ResourceHandler.resources.Keys) {
                stopResource(resourceName, false);
            }
            return true;
        }
        internal static object[] ConvertArrayToLuaTable(LuaTable table) {
            object[] result = new object[(int)table["Length"]];
            for (int i=0; i<result.Length; i++) {
                result[i] = table[i+1];
            }
            return result;
        }

        #region binding methods
        //Database
        internal static SQLiteConnection? dbConnect (string resourceName, string dbFile) {
            if (string.IsNullOrEmpty(dbFile) || string.IsNullOrWhiteSpace(dbFile))
                return null;
            if (!dbFile.StartsWith(':'))
                dbFile = Path.Combine(ResourceHandler.resources[resourceName], dbFile);
            else {
                dbFile = dbFile.Substring(1);
                string resName = dbFile.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (ResourceHandler.resources.ContainsKey(resName)) {
                    string resPath = ResourceHandler.resources[resName];
                    if (!string.IsNullOrEmpty(resPath)!) {
                        string pdPath = Path.GetDirectoryName(resPath)!;
                        if (!string.IsNullOrEmpty(pdPath))
                            dbFile = Path.Combine(pdPath, dbFile);
                    }
                } else {
                    Server.print($"Error: dbConnect resource '{resName}' doesn't exist.", ConsoleColor.Red);
                    return null;
                }
            }
            string fullPath = Path.GetFullPath(dbFile);
            if (!fullPath.StartsWith(Path.Combine(Server.mainServerPath, ResourceHandler.path), StringComparison.OrdinalIgnoreCase)) {
                Server.print($"Error: dbConnect resource '{fullPath}' is outside the allowed 'resources' directory.", ConsoleColor.Red);
                return null;
            }
            if (Path.GetDirectoryName(fullPath)!.Equals(Path.Combine(Server.mainServerPath, ResourceHandler.path))) {
                Server.print($"Error: dbConnect file must be inside a resource folder.", ConsoleColor.Red);
                return null;
            }
            if (string.IsNullOrEmpty(dbFile))
                return null;
            string rPath = Path.GetDirectoryName(dbFile)!;
            if (!string.IsNullOrEmpty(rPath))
                Directory.CreateDirectory(rPath);
            return LuaDatabase.dbConnect(dbFile);
        }
        internal static object? callExport(string resourceName, string func, params object[] arguments) {
            if (resourceName == null || func == null)
                return false;
            Lua? exLua = ResourceHandler.getLua(resourceName);
            if (exLua == null) {
                Server.print($"Error: trying to call '{func}' from resource '{resourceName}' but resource isn't running", ConsoleColor.Red);
                return false;
            }
            LuaFunction? exFunc = (LuaFunction)exLua[func];
            if (exFunc == null) {
                Server.print($"Error: trying to call '{func}' from resource '{resourceName}' but function does not exist.", ConsoleColor.Red);
                return false;
            }
            try {
                var queue = new TaskQueue();
                object? result = queue.EnqueueTask(() => {
                    return Task.FromResult(exFunc.Call(arguments));
                }).GetAwaiter().GetResult();
                if (result == null)
                    return false;
                return result;
            } catch (NLua.Exceptions.LuaScriptException luaEx) {
                if (luaEx.InnerException != null)
                    Server.print($"InnerException by {resourceName} using export: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                else
                    Server.print($"MainException by {resourceName} using export: {luaEx.Message}", ConsoleColor.DarkRed);
            } catch (NLua.Exceptions.LuaException luaEx) {
                if (luaEx.InnerException != null)
                    Server.print($"InnerException by {resourceName} using export: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                else
                    Server.print($"MainException by {resourceName} using export: {luaEx.Message}", ConsoleColor.DarkRed);
            } catch (Exception luaEx) {
                if (luaEx.InnerException != null)
                    Server.print($"InnerException by {resourceName} using export: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                else
                    Server.print($"MainException by {resourceName} using export: {luaEx.Message}", ConsoleColor.DarkRed);
            }
            return false;
        }
        //Events
        internal static object? addEvent(string eventName) {
            if (eventName == null)
                return false;
            if (customEvents.Contains(eventName)) {
                return false;
            }
            customEvents.Add(eventName);
            return true;
        }
        internal static object? addEventHandler(string eventName, LuaFunction func, Lua lua, string resourceName) {
            if (eventName == null || func == null)
                return false;
            if (!customEvents.Contains(eventName) && !eventNames.Contains(eventName)) {
                return false;
            } else if (isEventHandlerAdded(eventName, func, lua, resourceName) == true) {
                Server.print($"Warning: event '{eventName}' is already registered!");
                return false;
            }
            List<(string, object, string, LuaFunction)> list = new() {(eventName, "root", resourceName, func)};
            eventHandlers.Add(_eventHandlerNextID++, list);
            return true;
        }
        internal static bool? isEventHandlerAdded(string eventName, LuaFunction func, Lua lua, string resourceName) {
            if (eventName == null || func == null)
                return false;
            foreach (var kvp in eventHandlers) {
                List<(string, object, string, LuaFunction)> list = kvp.Value;
                foreach (var tuple in list) {
                    if (tuple.Item1.Equals(eventName) && tuple.Item2.Equals("root") && tuple.Item3.Equals(resourceName)) {
                        lua["isEventHandlerAddedCheckFunc"] = tuple.Item3;
                        lua["isEventHandlerAddedCheckFunc2"] = func;
                        string LuaFuncID = (string)lua.DoString("return tostring(isEventHandlerAddedCheckFunc)")[0];
                        string LuaFuncID2 = (string)lua.DoString("return tostring(isEventHandlerAddedCheckFunc2)")[0];
                        if (LuaFuncID.Equals(LuaFuncID2))
                            return true;
                    }
                }
            }
            return false;
        }
        internal static object? removeEventHandler(string eventName, LuaFunction func, Lua lua, string resourceName) {
            if (eventName == null || func == null)
                return false;
            object? res = isEventHandlerAdded(eventName, func, lua, resourceName);
            if (res == null || (bool)res == false) {
                Server.print($"Warning: event '{eventName}' is not assigned");
                return false;
            }
            foreach (var kvp in eventHandlers) {
                List<(string, object, string, LuaFunction)> list = kvp.Value;
                foreach (var tuple in list) {
                    if (tuple.Item1.Equals(eventName) && tuple.Item2.Equals("root") && tuple.Item4.Equals(func)) {
                        eventHandlers.Remove(kvp.Key);
                        return true;
                    }
                }
            }
            return false;
        }
        //Commands
        internal static object? addCommandHandler(string command, LuaFunction func, string resourceName) {
            bool? result = isCommandAlreadyhandled(command, func);
            if (result == null)
                return false;
            else if (result == true) {
                Server.print($"Warning: command: {command} is already handling {func}");
                return false;
            }
            List<(string, LuaFunction, string)> temp = new(){ (command, func, resourceName) };
            luaCommandHandlers.Add(luaCommandHandlers.Count, temp);
            return true;
        }
        internal static bool? isCommandAlreadyhandled(string command, LuaFunction func) {
            if (command == null || func == null)
                return false;
            foreach (var kvp in luaCommandHandlers) {
                foreach (var tuple in kvp.Value) {
                    if (tuple.Item1.Equals(command) && tuple.Item2.Equals(func)) {
                        return true;
                    }
                }
            }
            return false;
        }
        internal static object? removeCommandHandler(string command, LuaFunction func) {
            if (command == null || func == null)
                return false;
            object? res = isCommandAlreadyhandled(command, func);
            if (res == null || (bool)res == false) {
                Server.print($"Warning: command '{command}' is not assigned");
                return false;
            }
            foreach (var kvp in luaCommandHandlers) {
                List<(string, LuaFunction, string)> list = kvp.Value;
                foreach (var tuple in list) {
                    if (tuple.Item1.Equals(command) && tuple.Item2.Equals(func)) {
                        luaCommandHandlers.Remove(kvp.Key);
                        return true;
                    }
                }
            }
            return false;
        }
        internal static object? getData(string key) {
            if (key == null)
                return false;
            object? value = get(key);
            if (value == null) {
                return null;
            }
            return value;
        }
        internal static object? setData(string key, object? value) {
            if (key == null)
                return false;

            Type? type = null;

            if (value != null)
                type = value.GetType();

            string typeStr = type?.ToString() ?? "null"; // safe fallback
            return set(key, value, typeStr);
        }
        internal static LuaTable? getAllClientData(Client client, Lua lua) {
            if (client == null || lua == null) return default;
            LuaTable? temp = CreateTable(lua);
            temp = DictToLuaTable(lua, client.Data);
            Server.print($"{temp ?? CreateTable(lua)}");
            return temp ?? CreateTable(lua);
        }
        internal static object? getClientData(Client client, string key) {
            if (client == null || key == null) return default;
            if (client.Data.ContainsKey(key))
                return client.Data[key];
            return default;
        }
        internal static bool? setClientData(Client client, string key, object? value) {
            if (client == null || key == null) return default;
            if (client.Data.ContainsKey(key))
                if (value == null)
                    return client.Data.Remove(key);
                else
                    client.Data[key] = value;
            else if (value != null)
                client.Data.Add(key, value);
            return true;
        }
        internal static Client? getClientFromSessionID(string sessionID) {
            if (sessionID == null) return null;
            if (Server.clients.ContainsKey(sessionID))
                return Server.clients[sessionID];
            return null;
        }
        internal static LuaTable CreateTable(Lua lua) {
            return (LuaTable)lua.DoString("return {}")[0];
        }
        internal static LuaTable ConvertListToLuaTable(Lua lua, List<Dictionary<string, object>> list) {
            LuaTable luaTable = CreateTable(lua);

            for (int i = 0; i < list.Count; i++) {
                LuaTable innerTable = CreateTable(lua);
                foreach (var kvp in list[i]) {
                    var value = kvp.Value == DBNull.Value ? null : kvp.Value;
                    innerTable[kvp.Key] = value;
                }
                luaTable[i + 1] = innerTable;
            }
            return luaTable;
        }
        internal static LuaTable ConvertArrayToLuaTable(Lua lua, object[] strArray) {
            LuaTable luaTable = CreateTable(lua);
            for (int i=0; i<strArray.Length; i++) {
                luaTable[i+1] = strArray[i];
            }
            return luaTable;
        }
        public static LuaTable? DictToLuaTable(Lua lua, Dictionary<string, object>? dict) {
            if (dict == null) return default;
            LuaTable? table = CreateTable(lua);
            foreach (var kvp in dict) {
                if (kvp.Value is JsonElement elem) {
                    if (elem.ValueKind == JsonValueKind.Object)
                        table[kvp.Key] = DictToLuaTable(lua, System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>?>(elem.GetRawText()));
                    else if (elem.ValueKind == JsonValueKind.Array)
                        table[kvp.Key] = JsonArrayToLuaTable(lua, elem);
                    else
                        table[kvp.Key] = elem.ValueKind switch {
                            JsonValueKind.Number => elem.GetDouble(),  // Handle numbers
                            JsonValueKind.String => elem.GetString(), // Handle strings
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => elem.GetRawText()  // Fallback
                        };
                } else {
                    table[kvp.Key] = kvp.Value;
                }
            }
            return table;
        }
        public static LuaTable JsonArrayToLuaTable(Lua lua, JsonElement array) {
            LuaTable? table = CreateTable(lua);
            int i = 1;
            foreach (var elem in array.EnumerateArray()) {
                if (elem.ValueKind == JsonValueKind.Object)
                    table[i++] = DictToLuaTable(lua, System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>?>(elem.GetRawText()));
                else if (elem.ValueKind == JsonValueKind.Array)
                    table[i++] = JsonArrayToLuaTable(lua, elem);
                else
                    table[i++] = elem.ValueKind switch {
                        JsonValueKind.Number => elem.GetDouble(),  // Handle numbers
                        JsonValueKind.String => elem.GetString(), // Handle strings
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => elem.GetRawText()  // Fallback
                    };
            }
            return table;
        }
        public static string LuaTableToJson(LuaTable table) {
            object ConvertValue(object? value) {
                if (value is LuaTable lt) {
                    
                    bool isArray = true;
                    int count = 0;  
                    foreach (var key in lt.Keys) {
                        if (key is string strKey && int.TryParse(strKey, out int idx) && idx == count + 1) {
                            count++;
                        } else if (key is int idx2 && idx2 == count + 1) {
                            count++;
                        } else {
                            isArray = false;
                            break;
                        }
                    }

                    if (isArray) {
                        var list = new List<object?>();
                        for (int i = 1; i <= count; i++) {
                            list.Add(ConvertValue(lt[i]));
                        }
                        return list;
                    } else {
                        var dict = new Dictionary<string, object?>();
                        foreach (object entryObj in lt) {
                            var entry = (KeyValuePair<object, object>)entryObj;
                            dict[entry.Key.ToString()!] = ConvertValue(entry.Value);
                        }
                        return dict;
                    }
                } else if (value is string or bool or double or long or int or float or short or byte or  decimal or sbyte or ushort or uint or ulong or null) {
                    return value!;
                } else {
                    return value.ToString()!;
                }
            }

            var obj = ConvertValue(table);
            return System.Text.Json.JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        }
        internal static LuaTable getResources(Lua lua) {
            string[] resources = ResourceHandler.getAllResources();
            return ConvertArrayToLuaTable(lua, resources);
        }
        internal static LuaTable getClientsLuaTable(Lua lua) {
            return ConvertArrayToLuaTable(lua, getClients());
        }
        internal static void luaPrint(params object[] msgs) {
            if (msgs == null)
                return;
            string msg = "";
            foreach (object val in msgs)
                msg += val.ToString()+"\t";
            Server.luaPrint(msg);
        }
        internal static string GenerateRandomToken(int length = 32) {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
        internal static string? getInternalPath (string path, string resourceName) {
            if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrEmpty(resourceName) || string.IsNullOrWhiteSpace(resourceName)) {
                throw new Exception("Error: invalid variables");
            }
            if (!path.StartsWith(':')) {
                path = Path.Combine(ResourceHandler.resources[resourceName], path);
            } else {
                path = path.Substring(1);
                string resName = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (ResourceHandler.resources.ContainsKey(resName)) {
                    string resPath = ResourceHandler.resources[resName];
                    if (!string.IsNullOrEmpty(resPath)!) {
                        string pdPath = Path.GetDirectoryName(resPath)!;
                        if (!string.IsNullOrEmpty(pdPath))
                            path = Path.Combine(pdPath, path);
                    }
                }
            }
            string fullPath = Path.GetFullPath(path);

            if (!fullPath.StartsWith(Server.mainServerPath, StringComparison.InvariantCultureIgnoreCase))
                throw new Exception("Access denied: path outside allowed folder");
            return fullPath;
        }
        #endregion

        #region expose ML AI methods
        internal static MLContext newMlContext(int? num) {
            if (num != null) {
                Environment.SetEnvironmentVariable("DOTNET_MLNET_DISABLE_MKL", "1");
                return new MLContext(seed: num);
            }
            return new MLContext();
        }
        private static IDataView LoadCsv(MLContext mlContext, string path) {
            var columns = GetAllColumns(path);
            var loader = mlContext.Data.CreateTextLoader(new TextLoader.Options {
                Separators = [','],
                HasHeader = true,
                AllowQuoting = true,
                Columns = columns,
            });
            IDataView data = loader.Load(path);
            return data;
        }
        private static TextLoader.Column[] GetAllColumns(string path) {
            var header = File.ReadLines(path).First();
            var columnNames = header.Split(',');

            var columns = new List<TextLoader.Column>();
            for (int i = 0; i < columnNames.Length; i++) {
                string colName = columnNames[i].Trim();
                columns.Add(new TextLoader.Column(colName, DataKind.String, i));
            }
            return columns.ToArray();
        }
        internal static LuaTable? DataViewToDynamicRows(Lua lua, MLContext mlContext, string path, string resourceName) {
            path = getInternalPath(path, resourceName)!;
            if (path == null) return null;

            IDataView data = LoadCsv(mlContext, path);
            var rows = new List<DynamicRow>();
            var schema = data.Schema;

            using (var cursor = data.GetRowCursor(schema)) {
                // Build getters for all columns dynamically as ReadOnlyMemory<char>
                var getters = new List<ColumnGetter>();
                foreach (var col in schema) {
                    getters.Add(new ColumnGetter {
                        Name = col.Name,
                        RawType = typeof(ReadOnlyMemory<char>),
                        Getter = cursor.GetGetter<ReadOnlyMemory<char>>(col)
                    });
                }

                // Iterate rows
                while (cursor.MoveNext()) {
                    var row = new DynamicRow();
                    foreach (var g in getters) {
                        ReadOnlyMemory<char> rom = default;
                        ((ValueGetter<ReadOnlyMemory<char>>)g.Getter)(ref rom);
                        var str = rom.ToString();

                        // Dynamic parsing: try float, else keep string
                        if (float.TryParse(str, out float f))
                            row.Columns[g.Name] = f;
                        else
                            row.Columns[g.Name] = str;
                    }
                    rows.Add(row);
                }
            }

            // Convert to LuaTable
            LuaTable luaTable = CreateTable(lua);
            for (int i = 0; i < rows.Count; i++) {
                var row = rows[i];
                LuaTable rowTable = CreateTable(lua);
                foreach (var kvp in row.Columns) {
                    rowTable[kvp.Key] = kvp.Value ?? ""; // store empty string if null
                }
                luaTable[i + 1] = rowTable;
            }

            return luaTable;
        }
        #endregion
    }
}
internal class DynamicRow {
    public Dictionary<string, object> Columns { get; set; } = new();
}
internal class ColumnGetter {
    public string Name;
    public Type RawType;
    public Delegate Getter;
}