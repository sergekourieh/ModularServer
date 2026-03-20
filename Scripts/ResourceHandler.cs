using System.Xml.Linq;

namespace Server {

    public class ResourceHandler {
        #region Attributes
        internal enum resState {
            loaded,
            starting,
            stopping,
            restarting,
            running,
            failedToLoad,
            failedToRun,
        }
        internal readonly static string path = "resources";
        internal static Dictionary<string, string> resources = new(StringComparer.OrdinalIgnoreCase); //Name, directoryPath
        internal static Dictionary<string, resState> recsState = new(StringComparer.OrdinalIgnoreCase); //Name, state
        internal static Dictionary<string, LuaHandler> luaHandlerInstances = new(StringComparer.OrdinalIgnoreCase); //Name, instance
        #endregion

        internal static NLua.Lua? getLua(string resourceName) {
            if (!luaHandlerInstances.ContainsKey(resourceName))
                return null;
            return luaHandlerInstances[resourceName].getLua();
        }
        internal static void initiate() {
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }
        }
        internal static resState? getResourceState(string resourceName) {
            if (!recsState.ContainsKey(resourceName))
                return null;
            return recsState[resourceName];
        }
        internal static string[] getAllResources() {
            string[] result = new string[resources.Count];
            int i = 0;
            foreach (var kvp in resources) {
                result[i++] = kvp.Key;
            }
            return result;
        }
        internal static void setResourceState(string resourceName, resState state) {
            if (!recsState.ContainsKey(resourceName))
                return;
            recsState[resourceName] = state;
        }
        internal static void listResources() {
            foreach (string res in resources.Keys) {
                Server.print($"{res,-50} {recsState[res]}");
            }
        }
        internal static bool startResource(string resourceName) {
            if (!resources.ContainsKey(resourceName)) {
                Server.print($"Resource '{resourceName}' not found.");
                return false;
            } else if (recsState[resourceName] == resState.running) {
                Server.print($"Resource '{resourceName}' is already running.");
                return false;
            } else if (recsState[resourceName] == resState.failedToLoad) {
                Server.print($"Resource '{resourceName}' state is failedToLoad, use refresh command.");
                return false;
            }
            setResourceState(resourceName, resState.starting);
            if (luaHandlerInstances.ContainsKey(resourceName))
                return luaHandlerInstances[resourceName].startResource();
            LuaHandler luaHandling = new(resourceName);
            luaHandlerInstances.Add(resourceName, luaHandling);
            return luaHandling.startResource();
        }
        internal static bool restartResource(string resourceName) {
            if (!resources.ContainsKey(resourceName)) {
                Server.print($"Resource '{resourceName}' not found.");
                return false;
            } else if (recsState[resourceName] == resState.loaded) {
                Server.print($"Resource '{resourceName}' is not running.");
                return false;
            } else if (recsState[resourceName] == resState.failedToLoad) {
                Server.print($"Resource '{resourceName}' state is failedToLoad, use refresh command.");
                return false;
            }
            if (luaHandlerInstances.ContainsKey(resourceName)) {
                setResourceState(resourceName, resState.restarting);
                return luaHandlerInstances[resourceName].restartResource();
            }
            return false;//As it means that the resource is not in the list, which should be otherwise as the guard in the method above block it from reaching
        }
        internal static bool stopResource(string resourceName, bool print=true) {
            if (luaHandlerInstances.ContainsKey(resourceName)) {
                setResourceState(resourceName, resState.stopping);
                bool result = luaHandlerInstances[resourceName].stopResource(print);
                luaHandlerInstances.Remove(resourceName);
                return result;
            }
            return false;//As it means that the resource is not in the list, which should be otherwise as the guard in the method above block it from reaching
        }
        internal static void refreshResources(bool printNewAndRemoved = true) {
            Dictionary<string, string> resTemp = new();
            List<string> duplicates = new();
            try {
                Stack<string> stack = new(Directory.GetDirectories(path));
                while(stack.Count > 0) {
                    string directory = stack.Pop();
                    if (!(directory[path.Length+1] == '[' && directory[directory.Length-1] == ']')) {
                        string resourceName = Path.GetFileName(directory);
                        if (resTemp.ContainsKey(resourceName)) {
                            duplicates.Add(directory);
                        } else {
                            string metaPath = Path.Combine(directory, "meta.xml");
                            if (File.Exists(metaPath)) {
                                resTemp.Add(resourceName, directory);
                            }
                        }
                    } else {
                        string[] subDirectories = Directory.GetDirectories(directory);
                        foreach (string subDirectory in subDirectories) {
                            stack.Push(subDirectory);
                        }
                    }
                }
                if (duplicates.Count > 0) {
                    Server.print("$Duplicates detected:\n");
                    foreach (var dups in duplicates) {
                        Server.print(dups, timeDisplay: false);
                    }
                }
                var removedResources = resources.Keys.Except(resTemp.Keys).ToList();
                if (removedResources.Count > 0) {
                    Server.print($"Resource removed:");
                    foreach (var removed in removedResources) {
                        if (getResourceState(removed) == resState.running) {
                            LuaMethodsEvents.stopResource(removed, false);
                        }
                        resources.Remove(removed);
                        recsState.Remove(removed);
                        Server.print(removed);
                    }
                }
                foreach (var resource in resTemp)  {
                    if (!resources.ContainsKey(resource.Key) || (resources.ContainsKey(resource.Key) && getResourceState(resource.Key) == resState.failedToLoad)) {
                        bool wasFailedToLoad = false;
                        if (resources.ContainsKey(resource.Key) && getResourceState(resource.Key) == resState.failedToLoad)
                            wasFailedToLoad = true;
                        try {
                            //CHECK XML
                            var doc = XDocument.Load(Path.Combine(resource.Value, "meta.xml"));

                            var scripts = doc.Root!.Elements("script")
                                .Select(x => x.Attribute("src")?.Value)
                                .Where(x => x != null)
                                .ToList();
                            
                            if (scripts.Count < 1) {
                                Server.print($"Error on runtime {resource.Key}/meta.xml: there's no file.", ConsoleColor.Red);
                                setResourceState(resource.Key, resState.failedToLoad);
                                continue;
                            }
                            
                            var description = doc.Root!.Element("info")?.Attribute("description")?.Value ?? "";
                            var resourceInfo = new OpenAIAPI.Resource {
                                Name = resource.Key,
                                Description = description,
                            };
                            Server.allResources.Add(resourceInfo);

                            bool missingFile = false;
                            foreach (var src in scripts) {
                                if (!File.Exists(Path.Combine(resource.Value, src!))) {
                                    Server.print($"Error on loading {resource.Key}/meta.xml: {src} file not found.", ConsoleColor.Red, false);
                                    missingFile = true;
                                }
                            }

                            if (wasFailedToLoad) {
                                if (missingFile)
                                    setResourceState(resource.Key, resState.failedToLoad);
                                else
                                    setResourceState(resource.Key, resState.loaded);
                            } else {
                                resources.Add(resource.Key, resource.Value);
                                if (missingFile) {
                                    recsState.Add(resource.Key, resState.failedToLoad);
                                } else {
                                    recsState.Add(resource.Key, resState.loaded);
                                }
                            }
                        } catch (Exception ex) {
                            string safeMessage = ex.Message.Replace("\r", "").Replace("\n", " ");
                            Server.print($"{resource.Key}/meta.xml: {safeMessage}", ConsoleColor.Red, false);
                            continue;
                        }
                        if (getResourceState(resource.Key) == resState.loaded) {
                            if (printNewAndRemoved && !wasFailedToLoad) {
                                Server.print($"New resource detected: {resource.Key}");
                            } else if (wasFailedToLoad) {
                                Server.print($"Resource loaded: {resource.Key}");
                            }
                        }
                    }
                }
                if (printNewAndRemoved) {
                    Server.print("Resources has been refreshed.");
                }
            } catch (Exception ex) {
                Server.print(ex.Message, ConsoleColor.DarkMagenta);
            }
        }
    }
}