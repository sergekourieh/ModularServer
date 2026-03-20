using System.Text;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Net;

namespace Server {
    public class Client {
        #region Attributes
        internal TCP tcp;
        internal Dictionary<string, object> Data { get; } = new();
        #endregion
        internal Client() {
            tcp = new TCP();
        }
        #region shared methods with LUA
        public string getClientIP() {
            return tcp.clientIP;
        }
        public string getSessionID() {
            return tcp.getSessionID();
        }
        #endregion
        
        internal async Task initiate(TcpClient _socket, string sessionID, int receiveBufferSize, int sendBufferSize, Stream baseStream) {
            tcp.initiate(this, _socket, sessionID, receiveBufferSize, sendBufferSize, baseStream);
            await Task.Run(async () => {
                while (true) {
                    await Task.Delay(5000);
                    bool result = tcp.isConnected();
                    if (!result)
                        break;
                }
            });
        }

        internal class TCP {
            #region Attributes
            private TcpClient? socket;
            private Stream? stream;
            private byte[]? receiveBuffer;
            private Client client;
            internal string sessionID = "";
            private readonly byte[] discardBuffer = new byte[4096];
            internal long tick = Utility.getTick();
            internal string clientIP = "";
            #endregion
            #region get methods
                internal string getSessionID() {
                    return sessionID;
                }
            #endregion

            internal bool isConnected() {
                if (socket == null) return false;

                try {
                    Socket s = socket.Client; // get underlying socket
                    bool part1 = s.Poll(1000, SelectMode.SelectRead);
                    bool part2 = (s.Available == 0);
                    if (part1 && part2) {
                        _ = Disconnect("Lost connection");
                        return false; // disconnected
                    }
                    return true;
                } catch {
                    _ = Disconnect("Lost connection");
                    return false;
                }
            }

            internal enum msgTypes {
                error,
                info,
            }
            internal async void initiate(Client client, TcpClient _socket, string sessionID, int receiveBufferSize, int sendBufferSize, Stream baseStream) {
                this.client = client ?? throw new ArgumentNullException(nameof(client));;
                this.sessionID = sessionID;
                await connect(_socket, receiveBufferSize, sendBufferSize, baseStream);
            }
            internal async Task connect(TcpClient _socket, int receiveBufferSize, int sendBufferSize, Stream baseStream) {
                socket = _socket;
                socket.ReceiveBufferSize = receiveBufferSize;
                socket.SendBufferSize = sendBufferSize;
                stream = baseStream;
                receiveBuffer = new byte[receiveBufferSize];
                clientIP = getClientIP() ?? "";

                LuaMethodsEvents.onClientJoin(client);
                await InformClient(msgTypes.info, "Handshake successful");
                await ReceiveCallback();
            }
            internal async Task sendData(string json) {
                if (string.IsNullOrEmpty(json) || string.IsNullOrWhiteSpace(json)) {
                    return;
                }
                if (isConnected()) {
                    try {    
                        byte[] msg = Encoding.UTF8.GetBytes(json);
                        //byte[] lengthPrefix = BitConverter.GetBytes(msg.Length); //FIX send Length first
                        //stream.Write(lengthPrefix, 0, lengthPrefix.Length); //FIX send Length first
                        await stream!.WriteAsync(msg, 0, msg.Length);
                    } catch (Exception _ex) {
                        Server.print($"Error sending data to player '{clientIP}' via TCP: {_ex}", ConsoleColor.DarkMagenta);
                        await Disconnect("Lost Connection");
                    }
                } else {
                    await Disconnect();
                }
            }
            private async Task ReceiveCallback() {
                if (socket == null || stream == null)
                    return;
                try {
                    while (socket != null && stream != null) {
                        if (receiveBuffer == null)
                            return;
                        int bytesRead = await stream.ReadAsync(receiveBuffer, 0, 4);
                        if (bytesRead <= 0)
                            return;
                        int messageLength = BitConverter.ToInt32(receiveBuffer, 0);
                        if (messageLength <= 0) 
                            return;

                        byte[] messageBuffer = new byte[messageLength];
                        int totalBytesRead = 0;
                        if (messageLength > Server.TCPMaxPacketSize) {
                            Server.print($"Client {clientIP} sent invalid/too large packet ({messageLength} bytes)", ConsoleColor.Yellow);
                            // Discard the rest of the packet
                            int bytesToDiscard = messageLength;
                            while (bytesToDiscard > 0)
                            {
                                int chunk = Math.Min(discardBuffer.Length, bytesToDiscard);
                                int read = await stream.ReadAsync(discardBuffer, 0, chunk);
                                if (read <= 0) break; // client disconnected
                                bytesToDiscard -= read;
                            }
                            continue;
                        }
                        while (totalBytesRead < messageLength) {
                            bytesRead = await stream.ReadAsync(messageBuffer, totalBytesRead, messageLength - totalBytesRead);
                            if (bytesRead <= 0)
                                return; // Handle error or disconnection
                            
                            totalBytesRead += bytesRead;
                        }
                        if (Utility.getTick() > tick+Server.TCPcoolDownMS) {

                            string msg = Encoding.UTF8.GetString(messageBuffer);

                            HandleData(msg);
                            tick = Utility.getTick();
                        }
                    }
                } catch (Exception _ex) {
                    Server.print($"'{getClientIP()}': {_ex}", ConsoleColor.DarkMagenta);
                    await Disconnect();
                }
            }
            private void HandleData(string json) {
                Server.print($"Received From '{clientIP}': {json}");
                try {
                    LuaMethodsEvents.onClientSentData(client, json);
                } catch (Exception _ex) {
                    Server.print($"Exception {_ex}", ConsoleColor.DarkMagenta);
                }
            }
            private async Task InformClient(msgTypes type, string data, string from="Server", string time = "") {
                Dictionary<string, string> temp = new() {
                    {"type", type.ToString()},
                    {"data", data},
                    {"from", from},
                    {"sessionID", sessionID},
                    {"timestamp", time},
                };
                string json = JsonConvert.SerializeObject(temp);;
                await sendData(json);
            }
            internal string? getClientIP(bool includePort = false) {
                try {
                    if (socket?.Client?.RemoteEndPoint is IPEndPoint endPoint)
                    {
                        return includePort ? endPoint.ToString() : endPoint.Address.ToString(); // "endPoint returns wholes, endPoint.Address returns without port.
                    }
                }
                catch { }

                return default;
            }
            internal async Task<bool> Disconnect(string reason = "Client-side disconnection") {
                try {
                    if (socket == null)
                        return false;
                    Server.print($"Disconnected: {clientIP} {reason}.");
                    if (socket != null) {
                        if (stream != null) {
                            await stream.FlushAsync(); // Ensure all data is sent
                            stream.Close();
                        }
                        socket.Close();
                    }
                    LuaMethodsEvents.onClientDisconnect(client, reason);
                    Server.onClientDisconnect(sessionID);
                    stream = null;
                    receiveBuffer = null;
                    socket = null;
                    return true;
                } catch (Exception ex) {
                    Server.print(ex.Message, ConsoleColor.DarkMagenta);
                }
                return false;
            }
        }
    }
}