using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Xml.Linq;

namespace Server {
    public class Server {

        #region Attributes
        //Variables that can be set in config.ini file
        internal static int maxConPerIP = 5;
        internal static int tcpPort = 13000;
        internal static int udpPort = 13001;
        internal static int httpPort = 22000;
        internal static int UDPcoolDownMS = 200; //UDP Cooldown for receiving Data from a client
        internal static int TCPcoolDownMS = 1000; //TCP Cooldown for receiving Data from a client
        internal static int receiveTimeOut = 5000; //TCP receiveData timeout in Millisecond
        internal static int sendTimeOut = 5000; //TCP sendData timeout in Millisecond
        internal static int UDPReceiveBufferSize = 65536;
        internal static int UDPSendBufferSize = 65536;
        internal static int TCPReceiveBufferSize = 65536;
        internal static int TCPSendBufferSize = 65536;
        internal static int TCPMaxPacketSize = 65536;
        //End of config.ini variables
        internal static List<OpenAIAPI.Resource> allResources = new List<OpenAIAPI.Resource>();
        internal static OpenAIAPI openAIService;
        internal static bool chatGptEnabled = false;

        internal static TcpListener? tcpServer;
        internal static UdpClient? udpServer;
        internal static List<string> luaLog = new();
        internal static List<string> consoleLog = new();
        internal static string version = "1.0.0";
        internal static string title = "ModularServer";
        internal static char sign = '-';
        private static readonly object _queueLock = new object();
        internal static TimeSpan cooldown;
        private static ConcurrentDictionary<IPAddress, DateTime> lastRequestTime = new();
        private static Dictionary<IPAddress, int> tcpConnectionCounts = new();
        internal static HashSet<string> bannedIPs = new HashSet<string>();

        internal static Dictionary<string, Client> clients = new Dictionary<string, Client>();
        internal static Dictionary<string, IPEndPoint> udpClients = new Dictionary<string, IPEndPoint>();
        static Dictionary<string, string> commandsExplanation = new Dictionary<string, string>();
        private static CancellationTokenSource udpCts = new CancellationTokenSource();
        internal static string mainServerPath = Environment.CurrentDirectory;

        static bool useTLS = false;
        static string certPath = "server.pfx";
        static string certPassword = "";
        static X509Certificate2? serverCertificate = null;
        #endregion

        private static void InitialiseCommands() {
            commandsExplanation.Add("",
                "Available commands:\n\n" +
                "help\t\t\tShows all server's commands.\n" +
                "list\t\t\tList resources and their state.\n" +
                "start\t\t\tStart a loaded resource.\n" +
                "restart\t\t\tRestart a running resource.\n" +
                "stop\t\t\tStop a resource.\n" +
                "stopall\t\t\tStop all running resources.\n" +
                "refresh\t\t\tRefresh resource list to find new resources.\n" +
                "suggest\t\t\tAI suggests new resources. Use 'help suggest' for clarification.\n" +
                "shutdown\t\tStops accepting connections and shuts down the server.\n"
            );
            commandsExplanation.Add("help", "List the server's commands.\n\nHELP [command]\n\n\tcommand - displays help information on the command.\n");
            commandsExplanation.Add("list", "List resources and their state.\n");
            commandsExplanation.Add("start", "Start a loaded resoucre.\n\n\tUsage start <resource-name>\n");
            commandsExplanation.Add("restart", "Restart a running resoucre.\n\n\tUsage restart <resource-name>\n");
            commandsExplanation.Add("stop", "Stop a resoucre.\n\n\tUsage stop <resource-name>\n");
            commandsExplanation.Add("stopall", "Stop all running resources.\n");
            commandsExplanation.Add("refresh", "Refresh resource list to find new resources.\n");
            commandsExplanation.Add("suggest", "AI suggests new resources based on your currently loaded resources.\n\n\tFor best results, make sure each resource has a descriptive name and a clear description.\n");
            commandsExplanation.Add("shutdown", "Stops the server and shuts down the server.\n");
        }
        internal static async Task Main(string[] args) {

            try {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 5555);
                listener.Start();
            } catch (SocketException) {
                Console.Error.WriteLine("Another instance is running.");
                return;
            }


            Console.Title = title;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);//called when the user presses Ctrl+C or Ctrl+Break

            //Get Data from Configuration.xml!
            try {
                var doc = XDocument.Load("configuration.xml");
                var serverNode = doc.Element("XML")?.Element("SERVER");
                if (serverNode != null) {
                    int maxConPerIPxml = int.TryParse(serverNode.Element("MAX_CONNECTION_PER_IP")?.Value, out int val) ? val : maxConPerIP;
                    int tcpPortxml = int.TryParse(serverNode.Element("TCP_PORT")?.Value, out int val1) ? val1 : tcpPort;
                    int udpPortxml = int.TryParse(serverNode.Element("UDP_PORT")?.Value, out int val2) ? val2 : udpPort;
                    int httpPortxml = int.TryParse(serverNode.Element("HTTP_PORT")?.Value, out int val3) ? val3 : httpPort;
                    int coolDownMSxml = int.TryParse(serverNode.Element("UDP_COOLDOWN_MILLISECOND")?.Value, out int val4) ? val4 : UDPcoolDownMS;
                    int receiveTimeOutxml = int.TryParse(serverNode.Element("TCP_RECEIVE_TIME_OUT")?.Value, out int val5) ? val5 : receiveTimeOut;
                    int sendTimeOutxml = int.TryParse(serverNode.Element("TCP_SEND_TIME_OUT")?.Value, out int val6) ? val6 : sendTimeOut;
                    int UDPReceiveBufferSizexml = int.TryParse(serverNode.Element("UDP_RECEIVE_BUFFER_SIZE")?.Value, out int val7) ? val7 : UDPReceiveBufferSize;
                    int UDPSendBufferSizexml = int.TryParse(serverNode.Element("UDP_SEND_BUFFER_SIZE")?.Value, out int val8) ? val8 : UDPSendBufferSize;
                    int TCPReceiveBufferSizexml = int.TryParse(serverNode.Element("TCP_RECEIVE_BUFFER_SIZE")?.Value, out int val9) ? val9 : TCPReceiveBufferSize;
                    int TCPSendBufferSizexml = int.TryParse(serverNode.Element("TCP_SEND_BUFFER_SIZE")?.Value, out int val10) ? val10 : TCPSendBufferSize;
                    int TCPMaxPacketSizexml = int.TryParse(serverNode.Element("TCP_SEND_BUFFER_SIZE")?.Value, out int val11) ? val11 : TCPMaxPacketSize;

                    bool enableChatGPT = bool.TryParse(serverNode.Element("ENABLE_CHATGPT")?.Value, out bool val12) ? val12 : chatGptEnabled;
                    if (enableChatGPT) {
                        string chatgptAPI = serverNode.Element("CHATGPT_API")?.Value!;
                        if (!string.IsNullOrEmpty(chatgptAPI) && !string.IsNullOrWhiteSpace(chatgptAPI)) {
                            openAIService = new OpenAIAPI(chatgptAPI);
                            chatGptEnabled = true;
                        }
                    }

                    maxConPerIP = maxConPerIPxml;
                    tcpPort = tcpPortxml;
                    udpPort = udpPortxml;
                    httpPort = httpPortxml;
                    UDPcoolDownMS = coolDownMSxml;
                    receiveTimeOut = receiveTimeOutxml;
                    sendTimeOut = sendTimeOutxml;
                    UDPReceiveBufferSize = UDPReceiveBufferSizexml;
                    UDPSendBufferSize = UDPSendBufferSizexml;
                    TCPReceiveBufferSize = TCPReceiveBufferSizexml;
                    TCPSendBufferSize = TCPSendBufferSizexml;
                    TCPMaxPacketSize = TCPMaxPacketSizexml;
                }
                var sslNode = doc.Element("XML")?.Element("SSL");
                if (sslNode != null) {
                    bool useTLSxml = bool.Parse(sslNode.Element("ENABLE")?.Value ?? "false");
                    string certPathxml = sslNode.Element("CERTIFICATION_PATH")?.Value ?? "cert.pfx";
                    string certPasswordxml = sslNode.Element("CERTIFICATION_PASSWORD")?.Value ?? "";

                    useTLS=useTLSxml;
                    certPath=certPathxml;
                    certPassword=certPasswordxml;
                }
            } catch (Exception ex) {
                string safeMessage = ex.Message.Replace("\r", "").Replace("\n", " ");
                print($"Error in configuration.xml: {safeMessage}", ConsoleColor.Red);
                return;
            }

            //GET VALUES FROM CONFIG.INI
            
            cooldown = TimeSpan.FromMilliseconds(UDPcoolDownMS);

            InitialiseCommands();
            Database.initiateDatabase();
            ResourceHandler.initiate();
            LuaMethodsEvents.initiate();

            //PrintTopBar();
            print("==================================================================", timeDisplay: false);
            print($"= {title} {version}", timeDisplay: false);
            print("==================================================================", timeDisplay: false);
            print($"= Server IP address: auto ({getLocalIPAddress()})", timeDisplay: false);
            print($"= Server TCP port      : {tcpPort}", timeDisplay: false);
            print($"= Server UDP port      : {udpPort}", timeDisplay: false);
            //print($"= HTTP port        : {httpPort}", timeDisplay: false);
            print($"=", timeDisplay: false);
            print($"= Console Log file : ./logs/console.log", timeDisplay: false);
            print($"= Lua Log file : ./logs/lua.log", timeDisplay: false);
            print("==================================================================", timeDisplay: false);

            if (useTLS) {
                try {
                    serverCertificate = new X509Certificate2(certPath, certPassword);
                    if (!serverCertificate.HasPrivateKey) {
                        throw new Exception("Certificate does not have a private key!");
                    }
                } catch (Exception ex) {
                    print($"Failed to load TLS certificate: {ex.Message}");
                    useTLS = false; // or stop the server completely
                    return;        // stop initialization
                }
            }

            await Task.Factory.StartNew( async ()=> {//start tcp server on new thread
                await startTcpServer();
            });

            await Task.Factory.StartNew( async ()=> {//start udp server on new thread
                await startUdpServer();
            });

            await Task.Delay(TimeSpan.FromMilliseconds(200));
            print("To stop the server, type 'shutdown' or press Ctrl-C");
            print("Type 'help' for a list of commands.");
            await Task.Factory.StartNew(async ()=> {
                while(true) {
                    Console.Title = $"[{sign}] :: Users: {clients.Count} :: {ResourceHandler.resources.Count} resources";
                    if (sign == '-')
                        sign = '\\';
                    else if (sign == '\\')
                        sign = '|';
                    else if (sign == '|')
                        sign = '/';
                    else if (sign == '/')
                        sign = '-';
                    await Task.Delay(200);
                }
            });
            
            ResourceHandler.refreshResources(false);
            print($"Resources: {ResourceHandler.resources.Count} loaded");
            
            cmdPos:
            var originalForegroundColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            string? cmd = Console.ReadLine();
            Console.ForegroundColor = originalForegroundColor;
            if (!string.IsNullOrEmpty(cmd)) {
                if (cmd.StartsWith('/')) {
                    string[] luaArgs = cmd.Split(' ');
                    luaArgs[0] = luaArgs[0].TrimStart('/');
                    foreach (var list in LuaMethodsEvents.luaCommandHandlers) {
                        foreach (var tuple in list.Value) {
                            if (tuple.Item1.Equals(luaArgs[0])) {
                                try {
                                    var queue = new TaskQueue();
                                    queue.EnqueueTask(() => {
                                        return Task.FromResult(tuple.Item2.Call(luaArgs));
                                    }).GetAwaiter().GetResult();
                                } catch (NLua.Exceptions.LuaScriptException luaEx) {
                                    if (luaEx.InnerException != null)
                                        print($"InnerException by using /{luaArgs[0]}: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                                    else
                                        print($"MainException by using /{luaArgs[0]}: {luaEx.Message}", ConsoleColor.DarkRed);
                                } catch (NLua.Exceptions.LuaException luaEx) {
                                    if (luaEx.InnerException != null)
                                        print($"InnerException by using /{luaArgs[0]}: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                                    else
                                        print($"MainException by using /{luaArgs[0]}: {luaEx.Message}", ConsoleColor.DarkRed);
                                } catch (Exception luaEx) {
                                    if (luaEx.InnerException != null)
                                        print($"InnerException by using /{luaArgs[0]}: {luaEx.InnerException.Message}", ConsoleColor.DarkRed);
                                    else
                                        print($"MainException by using /{luaArgs[0]}: {luaEx.Message}", ConsoleColor.DarkRed);
                                }
                            }
                        }
                    }
                } else
                    help(cmd);
            }
            goto cmdPos;

        }
        private static async void help(string? input = null, bool once = false) {
            if (string.IsNullOrEmpty(input)) {
                print("help [command]");
                print(commandsExplanation[""]);
                return;
            }
            foreach (KeyValuePair<string, string> command in commandsExplanation) {
                if (input.ToLower().StartsWith(command.Key.ToLower()))
                {
                    string[] args = input.Split(' ');
                    if (args.Length < 1)
                        help();
                    else if (args[0] == "help") {
                        if (args.Length <= 1)
                            help();
                        else
                            if (commandsExplanation.ContainsKey(args[1].ToLower()))
                                print(commandsExplanation[args[1].ToLower()]);
                    } else if (once) {
                        if (commandsExplanation.ContainsKey(args[0]))
                            print(commandsExplanation[args[0]]);
                        return;
                    } else if (args[0] == "start") {
                        if (args.Length <= 1) {
                            help("start", true);
                        } else {
                            var queue = new TaskQueue();
                            bool result = queue.EnqueueTask(() =>  {
                                string? realName = ResourceHandler.resources.Keys.FirstOrDefault(k => k.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                                if (realName == null) {
                                    realName = args[1];
                                }
                                return Task.FromResult(LuaMethodsEvents.startResource(realName));
                            }).GetAwaiter().GetResult();
                        }
                    } else if (args[0] == "restart") {
                        if (args.Length <= 1) {
                            help("restart", true);
                        } else {
                            var queue = new TaskQueue();
                            bool result = queue.EnqueueTask(() =>  {
                                string? realName = ResourceHandler.resources.Keys.FirstOrDefault(k => k.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                                if (realName == null) {
                                    realName = args[1];
                                }
                                return Task.FromResult(LuaMethodsEvents.restartResource(realName));
                            }).GetAwaiter().GetResult();
                        }
                    } else if (args[0] == "stop") {
                        if (args.Length <= 1) {
                            help("stop", true);
                        } else {
                            var queue = new TaskQueue();
                            bool result = queue.EnqueueTask(() =>  {
                                string? realName = ResourceHandler.resources.Keys.FirstOrDefault(k => k.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                                if (realName == null) {
                                    realName = args[1];
                                }
                                return Task.FromResult(LuaMethodsEvents.stopResource(realName));
                            }).GetAwaiter().GetResult();
                        }
                    } else if (args[0] == "stopall") {
                        LuaMethodsEvents.stopAllResources();
                    } else if (args[0] == "list") {
                        ResourceHandler.listResources();
                    } else if (args[0] == "refresh") {
                        ResourceHandler.refreshResources();
                    } else if (args[0] == "shutdown") {
                        await shutdown();
                    } else if (args[0] == "suggest") {
                        suggestScripts();
                    } else
                        break;
                    return;
                }
            }
            print($"'{input.TrimStart()}' is not recognized as an command,\nWrite help to list the server's commands.\n");
        }
        private static async Task startUdpServer() {
            try {
                udpServer = new UdpClient(udpPort);
                udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, UDPReceiveBufferSize);
                udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, UDPSendBufferSize);

                print("UDP Server started and is ready to accept requests!");
                try {
                    while (!udpCts.Token.IsCancellationRequested) {
                        UdpReceiveResult result = await udpServer.ReceiveAsync(udpCts.Token);
                        string data = Encoding.UTF8.GetString(result.Buffer);
                        IPEndPoint clientEndpoint = result.RemoteEndPoint;
                        DateTime now = DateTime.UtcNow;

                        IPAddress ip = clientEndpoint.Address;
                        if (lastRequestTime.TryGetValue(ip, out DateTime lastTime)) {
                            if ((now - lastTime) < cooldown) {
                                Console.WriteLine($"[!] Blocked packet from {ip} - too fast");
                                continue;
                            }
                        }
                        lastRequestTime[ip] = now;
                        Console.WriteLine($"Received from {ip}: {data}");

                        if (data.Contains("sessionID") && !udpClients.ContainsValue(clientEndpoint)) {
                            string[] splt = data.Split(':', StringSplitOptions.None);
                            if (splt.Length < 2 || !clients.ContainsKey(splt[1]))
                                continue;
                            string sessionID = splt[1];
                            if (!udpClients.ContainsKey(sessionID)) {
                                udpClients.Add(splt[1], clientEndpoint);
                                LuaMethodsEvents.onUDPClientConnect(clientEndpoint, clients[sessionID]);
                                continue;
                            }
                        }

                        string sessionID2 = getSessionIDFromUdpClient(clientEndpoint);
                        LuaMethodsEvents.onUDPClientSentData(clientEndpoint, clients[sessionID2], data);
                    }
                } catch (OperationCanceledException) { //Expected, it will trigger because of the Cancellation we triggered. No need to output
                } catch (Exception ex) {
                    print($"UDP packet error: {ex}");
                }
            } catch(Exception e) {
                print(e.ToString());
            } finally {
                if (udpServer != null) {
                    udpServer.Dispose();
                    udpServer = null;
                }
            }
        }
        private static async Task startTcpServer() {
            tcpServer = null;
            try {
                IPAddress localAddr = IPAddress.Parse(getLocalIPAddress());
                tcpServer = new TcpListener(localAddr, tcpPort);
                tcpServer.Start();
                await Task.Run(() => tcpServer.BeginAcceptTcpClient(TCPConnectCallback, null));
                print("TCP Server started and is ready to accept connections!");
            } catch(SocketException e) {
                print($"SocketException: {e}", ConsoleColor.DarkMagenta);
            }
        }
        private static async void TCPConnectCallback(IAsyncResult _result) {
            if (tcpServer == null) return;
            TcpClient tcpClient = await Task.Run(() => tcpServer.EndAcceptTcpClient(_result));
            Stream baseStream = tcpClient.GetStream();
            if (useTLS) {
                var sslStream = new SslStream(baseStream, false);
                try {
                    await sslStream.AuthenticateAsServerAsync(serverCertificate!, clientCertificateRequired: false,
                        enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12, checkCertificateRevocation: true);
                    print($"TLS connection established with {tcpClient.Client.RemoteEndPoint}");
                    baseStream = sslStream;
                } catch (Exception ex) {
                    print($"TLS handshake failed: {ex.Message}", ConsoleColor.Red);
                    tcpClient.Close();
                    return;
                }
            }

            tcpServer.BeginAcceptTcpClient(TCPConnectCallback, null);
            if (tcpClient.Client.RemoteEndPoint != null) {
                print($"Server: Incoming connection from {tcpClient.Client.RemoteEndPoint}...");
            }

            #region Limit same IP connections + Ban 
            IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
            if (remoteEndPoint == null) {
                return;
            }
            IPAddress ip = remoteEndPoint.Address;
            if (bannedIPs.Contains(ip.ToString())) {
                sendErrorMsg(tcpClient, "Banned");
                return;
            }
            if (!tcpConnectionCounts.ContainsKey(ip))
                tcpConnectionCounts[ip] = 0;
            
            if (tcpConnectionCounts[ip] != 0 && tcpConnectionCounts[ip] >= maxConPerIP) { // if it's 0, ignore
                print($"Too many connections from {ip}");
                sendErrorMsg(tcpClient, "Too many connections from your IP address. Please try again later.");
                return;
            }
            tcpConnectionCounts[ip]++;
            #endregion
            
            tcpClient.ReceiveTimeout = receiveTimeOut;  // Timeout for receiving data
            tcpClient.SendTimeout = sendTimeOut;     // Timeout for sending data

            Client? hC = new Client();
            string sessionID = Guid.NewGuid().ToString();
            clients.Add(sessionID, hC);
            await Task.Factory.StartNew(async ()=> {//Run new client on separated thread
                await hC.initiate(tcpClient, sessionID, TCPReceiveBufferSize, TCPSendBufferSize, baseStream);
            });
        }
        /*private static async void startHTTPServer() { }*/
        private static void sendErrorMsg(TcpClient tcpClient, string msg) {
            try {
                byte[] message = Encoding.UTF8.GetBytes(msg);
                NetworkStream stream = tcpClient.GetStream();
                stream.Write(message, 0, message.Length);
                stream.Flush();
                stream.Close();
            } catch (Exception ex) {
                print($"Error sending message to client: {ex.Message}");
            }
        }
        private static async Task stopServer(){
            if (tcpServer != null) {
                foreach(var client in clients.ToList()) {
                    await client.Value.tcp.Disconnect();
                }
                tcpServer.Stop();
                tcpServer = null;

                print("TCP Server Stopped.");
            }
            if (udpServer != null) {
                udpCts.Cancel();
                print("UDP Server Stopped.");
            }
        }
        private static async Task shutdown(){
            if (tcpServer != null)
                await stopServer();
            LuaMethodsEvents.stopAllResources();
            print("Stopping resources........");
            saveLogs();
            Environment.Exit(0);
        }
        private static void saveLogs() {
            writeLogsToFile("console", consoleLog.Count%10);//if its 10, than it's equal 0, does nothing. Otherwise, writes the logs that were not written
            writeLogsToFile("lua", luaLog.Count%10);
        }
        private static async void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
            e.Cancel = true;
            await shutdown();
        }
        internal static void onClientDisconnect(string sessionID) {
            if (clients.ContainsKey(sessionID)) {
                IPAddress ip = IPAddress.Parse(clients[sessionID].getClientIP());
                if (ip != null)
                    if (tcpConnectionCounts.ContainsKey(ip))
                        tcpConnectionCounts[ip]--;
                clients.Remove(sessionID);
            }
            if (udpClients.ContainsKey(sessionID))
                udpClients.Remove(sessionID);
        }
        private static string getLocalIPAddress() {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList) {
                if (ip.AddressFamily == AddressFamily.InterNetwork) {
                    return IPAddress.Any.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
        internal static string getSessionIDFromTcpClient(Client client) {
            foreach (var c in clients) {
                if (c.Value == client) {
                    return c.Key;
                }
            }
            return "";
        }
        internal static string getSessionIDFromUdpClient(IPEndPoint client) {
            foreach (var c in udpClients) {
                if (c.Value == client) {
                    return c.Key;
                }
            }
            return "";
        }
        internal static void print(string msg, ConsoleColor cc = ConsoleColor.Gray, bool timeDisplay = true, bool write = false, bool color = true) {
            lock (_queueLock) {
                if (timeDisplay) {
                    msg = $"[{Utility.printTick()}] {msg}";
                }
                if (color) {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = cc;
                }
                if (write) {
                    Console.Write(msg);
                } else {
                    Console.WriteLine(msg);
                }
                if (color) {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    consoleLog.Add($"{Utility.getTickCount()} {msg}");
                }
                if (consoleLog.Count % 10 == 0)
                    writeLogsToFile("console", 10);
            }
        }
        internal static void luaPrint(string msg) {
            lock (_queueLock) {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(msg);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                luaLog.Add($"{Utility.getTickCount()} {msg}");
                if (luaLog.Count % 10 == 0)
                    writeLogsToFile("lua", 10);
            }
        }
        internal static void writeLogsToFile(string which, int num) {
            if (num == 0)
                return;
            List<string> temp;
            if (which == "lua")
                temp = luaLog;
            else
                temp = consoleLog;
            if (temp.Count % num == 0) { //Each times 'num' lines are added, it saves them
                if (!Directory.Exists("logs")) {
                    Directory.CreateDirectory("logs");
                }
                string filePath = Path.Combine("logs", which+".log");
                using (StreamWriter writer = new StreamWriter(filePath, true, Encoding.UTF8)) {
                    foreach (var line in getLastMsgs(temp, num)) {
                        writer.WriteLine(line);
                    }
                }
            }
        }
        internal static string[] getLastMsgs(List<string> list, int num = 10)  {
            var lastTenItems = list.Skip(Math.Max(0, list.Count - num)).Take(num).ToList();
            string[] temp = new string[num];
            int i=0;
            foreach (var item in lastTenItems)
                temp[i++] = item;
            return temp;
        }
        internal static async void suggestScripts() {
            // Example usage
            if (openAIService == null) {
                print("OpenAI Error: Either disabled or API Key wasn't given/wrong.");
                return;
            }
            print("Waiting for the AI response...");
            var suggestions = await openAIService.SuggestScriptsAsync(allResources);
            print("Suggestion for new resources based on your resources:", ConsoleColor.DarkBlue);
            foreach (var s in suggestions)
            {
                print($"Name: ", ConsoleColor.DarkCyan, false, true);
                print($"{s.ScriptName}", ConsoleColor.Cyan, false);

                print($"Purpose: ", ConsoleColor.DarkYellow, false, true);
                print($"{s.Purpose}", ConsoleColor.Yellow, false);

                print($"Description: ", ConsoleColor.DarkGray, false, true);
                print($"{s.Description}", ConsoleColor.Gray, false);

                print($"Related: ", ConsoleColor.DarkGreen, false, true);
                print($"{string.Join(", ", s.RelatedResources)}", ConsoleColor.Green, false);
                print("---", ConsoleColor.DarkGray, false);
            }

        }
    }
}