using NLua;
using System.Xml;
using System.Xml.Linq;

namespace Server {
    public class LuaHandler {
        #region Attributes
        private Lua lua;
        private LuaBindings LuaBinds;
        private string resourceName;
        #endregion
        internal LuaHandler(string resourceName) {
            this.resourceName = resourceName;
            lua = new();
            StaticTaskQueue.initialize(resourceName);
            LuaBinds = new LuaBindings(lua, resourceName);
        }
        internal Lua getLua() {
            return lua;
        }
        internal bool startResource() {
            try {
                if (LuaMethodsEvents.activeResources.ContainsKey(resourceName)) {
                    Server.print($"Resource '{resourceName}' is already running.");
                    return false;
                }
                string directory = ResourceHandler.resources[resourceName];
                var doc = XDocument.Load(Path.Combine(directory, "meta.xml"));

                var scripts = doc.Root!.Elements("script")
                    .Select(x => x.Attribute("src")?.Value)
                    .Where(x => x != null)
                    .ToList();
                if (scripts.Count < 1) {
                    Server.print($"Error on runtime {resourceName}/meta.xml: there's no file.", ConsoleColor.Red);
                    ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToLoad);
                    return false;
                }
                var loadedFunctions = new List<LuaFunction>();
                Server.print($"Starting resource: '{resourceName}'");

                lua.NewTable(resourceName);
                lua.DoString($@"setmetatable({resourceName}, {{ __index = _G }})");
                foreach (var scriptFile in scripts) {
                    try {
                        string code = File.ReadAllText(Path.Combine(directory, scriptFile!));
                        string wrappedCode = $@" local _ENV = {resourceName} {code} ";
                        LuaFunction func = lua.LoadString(wrappedCode,  $"{resourceName}");
                        func.Call();
                        loadedFunctions.Add(func);
                    } catch (FileNotFoundException) {
                        Server.print($"Error on runtime {resourceName}/{scriptFile}: file not found.", ConsoleColor.Red);
                        ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToLoad);
                        return false;
                    }
                }
                ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.running);
                LuaMethodsEvents.activeResources[resourceName] = loadedFunctions;
                return true;
            } catch (XmlException ex) {
                string safeMessage = ex.Message.Replace("\r", "").Replace("\n", " ");
                Server.print($"XmlError at runtime {resourceName}/meta.xml: {safeMessage}", ConsoleColor.Red);
                ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToLoad);
            } catch (NLua.Exceptions.LuaScriptException luaEx) {
                if (luaEx.InnerException != null)
                    Server.print($"InnerException at runtime: {luaEx.InnerException.Message}", ConsoleColor.Red);
                else
                    Server.print($"MainException at runtime: {luaEx.Message}", ConsoleColor.Red);
                ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToRun);
            } catch (NLua.Exceptions.LuaException luaEx) {
                if (luaEx.InnerException != null)
                    Server.print($"InnerException at runtime: {luaEx.InnerException.Message}", ConsoleColor.Red);
                else
                    Server.print($"MainException at runtime: {luaEx.Message}", ConsoleColor.Red);
                ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToRun);
            } catch (Exception ex) {
                Server.print($"Error starting resource '{resourceName}': {ex.Message}", ConsoleColor.DarkMagenta);
                ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.failedToLoad);
            }
            Server.print($"Failed to start resource: '{resourceName}'");
            return false;
        }
        internal bool restartResource() {
            try {
                bool stopResult = LuaMethodsEvents.stopResource(resourceName);
                if (!stopResult)
                    return false;
                return LuaMethodsEvents.startResource(resourceName);
            } catch (Exception ex) {
                Server.print($"Error restarting resource '{resourceName}': {ex.Message}", ConsoleColor.DarkMagenta);
            }
            return false;
        }
        internal bool stopResource(bool print = true) {
            try {
                if (LuaMethodsEvents.activeResources.ContainsKey(resourceName)) {
                    foreach(var scripts in LuaMethodsEvents.activeResources[resourceName])
                        scripts.Dispose();
                    LuaMethodsEvents.activeResources.Remove(resourceName);
                    ResourceHandler.setResourceState(resourceName, ResourceHandler.resState.loaded);
                    if (print)
                        Server.print($"Stopped resource: '{resourceName}'");
                    return true;
                } else {
                    if (print)
                        Server.print($"Resource '{resourceName}' is not running.");
                }
            } catch (Exception ex) {
                Server.print($"Error Stopping resource '{resourceName}': {ex.Message}", ConsoleColor.DarkMagenta);
            }
            return false;
        }
    }
}