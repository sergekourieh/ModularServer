using NLua;
using System.Net;
using System.Data.SQLite;
using System.Text.Json;
using Microsoft.ML;

namespace Server {
    public class LuaBindings {
        #region Attributes
        private Lua lua;
        private string resourceName;
        #endregion
        internal LuaBindings(Lua lua, string resourceName) {
            this.lua = lua;
            this.resourceName = resourceName;

            #region Disable for security
            lua["os"] = null;             // system commands, environment
            lua["dofile"] = null;         // arbitrary file execution
            lua["loadfile"] = null;       // load external files
            lua["load"] = null;           // compile code at runtime
            lua["debug"] = null;          // stack inspection / sandbox escape
            lua["require"] = null;        // import external modules
            lua["package"] = null;        // module loading
            lua["collectgarbage"] = null; // can manipulate memory
            lua["luanet"] = null;
            lua["CLR"] = null;
            lua["system"] = null;         // if present
            #endregion
            #region definitions
            lua["clients"] = LuaMethodsEvents.getClientsLuaTable(lua);
            lua["root"] = "root";
            lua["resourceName"] = resourceName;
            lua["resources"] = LuaMethodsEvents.getResources(lua);
            #endregion
            #region LUA Database
            lua["dbConnect"] = new Func<string, SQLiteConnection?>((query) => {return LuaMethodsEvents.dbConnect(resourceName, query);});
            lua["dbExec"] = new Func<SQLiteConnection, string, object[], bool>(LuaDatabase.dbExec);
            lua["dbQuery"] = new Func<SQLiteConnection, string, object[], LuaTable>(dbQuery);
            lua["dbClose"] = new Action<SQLiteConnection>(LuaDatabase.dbClose);
            #endregion
            #region server methods
            lua["print"] = new Action<object[]>(LuaMethodsEvents.luaPrint);
            #endregion
            #region exports
            lua["call"] = new Func<string, string, object[], object?>(LuaMethodsEvents.callExport);
            #endregion
            #region general
            lua["getTick"] = new Func<long>(Utility.getTick);
            lua["get"] = new Func<string, object?>(LuaMethodsEvents.getData);
            lua["set"] = new Func<string, object, object?>(LuaMethodsEvents.setData);
            #endregion
            #region client
            lua["getSessionID"] = new Func<Client, string> ( (Client client) => { return client.getSessionID(); });
            lua["getClientFromSessionID"] = new Func<string, Client?>(LuaMethodsEvents.getClientFromSessionID);
            lua["sendTCPToClient"] = new Action<Client, string>(LuaMethodsEvents.sendTCPToClient);
            lua["sendUDPToClient"] = new Action<IPEndPoint, string>(LuaMethodsEvents.sendUDPToClient);
            lua["getClientsCount"] = new Func<int>(LuaMethodsEvents.getClientsCount);
            lua["kickClient"] = new Func<Client, string?, bool>(LuaMethodsEvents.kickClient);
            lua["getAllClientData"] = new Func<Client, LuaTable?>((Client client) => { return LuaMethodsEvents.getAllClientData(client, lua); });
            lua["getClientData"] = new Func<Client, string, object?>(LuaMethodsEvents.getClientData);
            lua["setClientData"] = new Func<Client, string, object?, bool?>(LuaMethodsEvents.setClientData);
            #endregion
            #region IP Ban
            lua["BanIP"] = new Func<string, bool>(LuaMethodsEvents.BanIP); 
            lua["unBanIP"] = new Func<string, bool>(LuaMethodsEvents.unBanIP);
            lua["isBanned"] = new Func<string, bool>(LuaMethodsEvents.IsBanned);
            #endregion
            #region Cryptography
            lua["hashIt"] = new Func<string, string>(PasswordHasher.HashPassword);
            lua["checkHash"] = new Func<string, string, bool>(PasswordHasher.VerifyPassword);
            lua["generateRandomToken"] = new Func<int, string>(LuaMethodsEvents.GenerateRandomToken);
            lua["generateGuid"] = new Func<string>(() => Guid.NewGuid().ToString());
            #endregion
            #region JSON
            lua["toJSON"] = new Func<LuaTable, string?>(LuaMethodsEvents.LuaTableToJson);
            lua["fromJSON"] = new Func<string, LuaTable?>((json) => LuaMethodsEvents.DictToLuaTable(lua, JsonSerializer.Deserialize<Dictionary<string, object>?>(json)));
            //Should add IsJson or IsJsonParsable, or ParseJson
            #endregion
            #region resources
            lua["startResource"] = new Func<string, bool>((string resourceName) => {
                var queue = new TaskQueue();
                bool result = queue.EnqueueTask(() =>
                {
                    return Task.FromResult(LuaMethodsEvents.startResource(resourceName));
                }).GetAwaiter().GetResult();
                return result || false;
            });
            lua["restartResource"] = new Func<string, bool>((string resourceName) => {
                var queue = new TaskQueue();
                bool result = queue.EnqueueTask(() =>
                {
                    return Task.FromResult(LuaMethodsEvents.restartResource(resourceName));
                }).GetAwaiter().GetResult();
                return result || false;
            });
            lua["stopResource"] = new Func<string, bool>((string resourceName) => {
                var queue = new TaskQueue();
                bool result = queue.EnqueueTask(() =>
                {
                    return Task.FromResult(LuaMethodsEvents.stopResource(resourceName));
                }).GetAwaiter().GetResult();
                return result || false;
            });
            lua["getResourceState"] = new Func<string, string?>((string resourceName) => {
                return ResourceHandler.getResourceState(resourceName).ToString();
            });
            #endregion
            #region event methods
            lua["addEvent"] = new Func<string, object?>(LuaMethodsEvents.addEvent);
            lua["addEventHandler"] = new Func<string, LuaFunction, object?>((string eventName, LuaFunction func) => {return LuaMethodsEvents.addEventHandler(eventName, func, lua, resourceName);});
            lua["isEventHandlerAdded"] = new Func<string, LuaFunction, bool?>((string eventName, LuaFunction func) => {return  LuaMethodsEvents.isEventHandlerAdded(eventName, func, lua, resourceName);});
            lua["removeEventHandler"] = new Func<string, LuaFunction, object?>((string eventName, LuaFunction func) => {return  LuaMethodsEvents.removeEventHandler(eventName, func, lua, resourceName);});
            lua["triggerEvent"] = new Func<string, object[], object?>(triggerEvent);
            #endregion
            #region command handler
            lua["addCommandHandler"] = new Func<string, LuaFunction, object?>((string command, LuaFunction func) => {return LuaMethodsEvents.addCommandHandler(command, func, resourceName);});
            lua["removeCommandHandler"] = new Func<string, LuaFunction, object?>(LuaMethodsEvents.removeCommandHandler);
            lua["isCommandAlreadyhandled"] = new Func<string, LuaFunction, bool?>(LuaMethodsEvents.isCommandAlreadyhandled);
            #endregion
            #region AI
            lua["chatGPT"] = new Func<string, object?>((string question) => {
                if (!Server.chatGptEnabled)
                    return default;
                return Server.openAIService.chatGPT(question);
            });
            lua["newMLContext"] = new Func<int?, object?>(LuaMethodsEvents.newMlContext);
            lua["LoadCsv"] = new Func<MLContext, string, object?>((MLContext ml, string path)=>{
                return LuaMethodsEvents.DataViewToDynamicRows(lua, ml, path, resourceName);
            });
            lua["CS_TrainAndForecast"] = new Func<MLContext, LuaTable, int, int, int, float, object?>((MLContext ml, LuaTable data, int windowSize, int seriesLength, int horizonValue, float confidence)=>{
                return TimeSeriesForecaster.CS_TrainAndForecast(ml, lua, data, windowSize, seriesLength, horizonValue, confidence);
            });
            #endregion
        }
        internal LuaTable dbQuery (SQLiteConnection luaSql, string query, params object[] parameters) {
            var result = LuaDatabase.dbQuery(luaSql, query, parameters);
            return LuaMethodsEvents.ConvertListToLuaTable(lua, result);
        }
        internal object? triggerEvent(string eventName, params object[] arguments) {
            if (eventName == null || arguments == null)
                return false;
            if (!LuaMethodsEvents.eventNames.Contains(eventName)) {
                return false;
            }
            object? result = null;
            foreach (var kvp in LuaMethodsEvents.eventHandlers) {
                List<(string, object, string, LuaFunction)> list = kvp.Value;
                foreach (var tuple in list) {
                    if (tuple.Item1.Equals(eventName) && tuple.Item2.Equals("root")) {
                        StaticTaskQueue.AddLuaTaskToQueue(tuple.Item3, tuple.Item1, (string)tuple.Item2, tuple.Item4, arguments);
                        result = true;
                    }
                }
            }
            return result;
        }
    }
}
