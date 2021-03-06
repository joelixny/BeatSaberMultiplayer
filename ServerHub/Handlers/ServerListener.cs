﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ServerCommons.Data;
using ServerCommons.Misc;
using Settings = ServerHub.Misc.Settings;
using Math = ServerCommons.Misc.Math;
using System.Threading.Tasks;
using Open.Nat;

namespace ServerHub.Handlers {
    public class ServerListener {
        private TcpListener Listener { get; set; } = new TcpListener(IPAddress.Any, Settings.Instance.Server.Port);

        public bool Listen { get; set; }

        public List<ClientObject> ConnectedClients { get; set; } = new List<ClientObject>();

        private string TitleFormat = "ServerHub - {0} Servers Connected";
        
        public class ClientObject {
            public CancellationTokenSource CancellationTokenSource { get; set; }
            public Data Data { get; set; }
        }

        public ServerListener() {
            Console.Title = string.Format(TitleFormat, 0);
        }

        public void RemoveClient(ClientObject cO) {
            if(cO.Data.ID!=-1) Logger.Instance.Log($"Unregistered Server [{cO.Data.Name}] @ [{cO.Data.IPv4}:{cO.Data.Port}]");
            ConnectedClients.Remove(cO);
            cO.Data.TcpClient.Close();
            cO.CancellationTokenSource.Cancel();
            
            Console.Title = string.Format(TitleFormat, ConnectedClients.Count(o => o.Data.ID != -1));
        }
        
        public void AddClient(ClientObject cO) {
            ConnectedClients.Add(cO);
            Console.Title = string.Format(TitleFormat, ConnectedClients.Count(o => o.Data.ID != -1));
        }

        public async void StartAsync() {
            Listen = true;

            if (Settings.Instance.Server.TryUPnP)
            {
                Logger.Instance.Log($"Trying to open port {Settings.Instance.Server.Port} using UPnP...");
                try
                {
                    NatDiscoverer discoverer = new NatDiscoverer();
                    CancellationTokenSource cts = new CancellationTokenSource(2500);
                    NatDevice device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

                    await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, Settings.Instance.Server.Port, Settings.Instance.Server.Port, "BeatSaber Multiplayer ServerHub"));

                    Logger.Instance.Log($"Port {Settings.Instance.Server.Port} is open!");
                }
                catch (Exception)
                {
                    Logger.Instance.Warning($"Can't open port {Settings.Instance.Server.Port} using UPnP!");
                }
            }

            Listener.Start();
            BeginListening();
        }

        async void BeginListening() {
            while (Listen) {
                Data data = await AcceptClient();
                if (data.TcpClient == null) continue;

                CancellationTokenSource cts = new CancellationTokenSource();
                ThreadPool.QueueUserWorkItem(new WaitCallback(obj => {
                    CancellationTokenSource token = (CancellationTokenSource)obj;
                    var clientObject = new ClientObject {CancellationTokenSource = token, Data = data};
                    int nullPackets = 0;
                    while (!token.IsCancellationRequested) {
                        if (!clientObject.Data.TcpClient.Connected) {
                            RemoveClient(clientObject);
                            break;
                        }
                        try
                        {
                            if (!PacketHandler(ListenForPackets(ref clientObject), ref clientObject)) nullPackets++;
                        }catch(Exception)
                        {
                            Logger.Instance.Warning($"Lost connection to server @ {clientObject.Data.IPv4}:{clientObject.Data.Port}");
                            cts.Cancel();
                        }
                        if (nullPackets >= 128)
                        {
                            Logger.Instance.Warning($"Lost connection to server @ {clientObject.Data.IPv4}:{clientObject.Data.Port}");
                            cts.Cancel();
                        }
                    }
                    cts.Dispose();
                    }), cts);
            }
        }

        async Task<Data> AcceptClient() {
            Logger.Instance.Log("Waiting for a connection");
            TcpClient client;
            try
            {
                client = await Listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                client = null;
            }
            return new Data {TcpClient = client};
        }

        /// <summary>
        /// Returns the Packet of data sent by a TcpClient
        /// </summary>
        /// <param name="serverData">Returns the ServerData object if the packet was a ServerDataPacket</param>
        /// <returns>The Packet sent</returns>
        IDataPacket ListenForPackets(ref ClientObject cO) {
            var client = cO.Data.TcpClient;
            byte[] bytes = new byte[Packet.MAX_BYTE_LENGTH];
            IDataPacket packet = null;
            try
            {
                if (client.GetStream().Read(bytes, 0, bytes.Length) != 0)
                {
                    packet = Packet.ToPacket(bytes);
                }
                else
                {
                    Thread.Sleep(5);
                }

                if (packet is ServerDataPacket) {
                    var sPacket = (ServerDataPacket) packet;
                    if (sPacket.FirstConnect)
                    {
                        int duplicates = ConnectedClients.Count(x => (x.Data.Name == sPacket.Name) && (x.Data.Port == sPacket.Port) && (x.Data.IPv4 == sPacket.IPv4));
                        if (duplicates > 0)
                        {
                            ConnectedClients.Where(x => (x.Data.Name == sPacket.Name) && (x.Data.Port == sPacket.Port) && (x.Data.IPv4 == sPacket.IPv4)).AsParallel().ForAll(x => RemoveClient(x));
                            Logger.Instance.Log($"Removed {duplicates} duplicate{(duplicates > 1?"s":"")}");
                        }

                        cO.Data = new Data(ConnectedClients.Count(x => x.Data.ID != -1) == 0 ? 0 : ConnectedClients.Last(x => x.Data.ID != -1).Data.ID + 1) { TcpClient = client, FirstConnect = sPacket.FirstConnect, IPv4 = sPacket.IPv4, Name = sPacket.Name, Port = sPacket.Port, MaxPlayers = sPacket.MaxPlayers };
                        AddClient(cO);

                        Logger.Instance.Log($"Registered Server [{cO.Data.Name}] @ [{cO.Data.IPv4}:{cO.Data.Port}]");
                    }
                    else
                    {
                        
                    }
                }

                return packet;
            }
            catch (Exception)
            {
                return null;
            }
        }

        bool PacketHandler(IDataPacket dataPacket, ref ClientObject cO) {
            if(dataPacket == null)
            {
                return false;
            }
            switch (dataPacket.ConnectionType) {
                case ConnectionType.Client:
                    var clientData = (ClientDataPacket)dataPacket;
                    clientData.Servers = GetServers(clientData.Offset);
                    if (cO.Data.TcpClient != null && cO.Data.TcpClient.Connected)
                    {
                        cO.Data.TcpClient.GetStream().Write(clientData.ToBytes(), 0, clientData.ToBytes().Length);
                        cO.Data.TcpClient.Close();
                        cO.CancellationTokenSource.Cancel();
                    }
                    else
                    {
                        cO.CancellationTokenSource.Cancel();
                    }
                    break;
                case ConnectionType.Server:
                    var serverData = (ServerDataPacket)dataPacket;
                    if (serverData.RemoveFromCollection) {
                        var temp = cO.Data;
                        RemoveClient(ConnectedClients.First(o => o.Data.ID == temp.ID));
                        cO.CancellationTokenSource.Cancel();
                        break;
                    }
                    cO.Data.IPv4 = serverData.IPv4;
                    cO.Data.Name = serverData.Name;
                    cO.Data.Port = serverData.Port;
                    cO.Data.MaxPlayers = serverData.MaxPlayers;
                    cO.Data.Players = serverData.Players;
                    if (serverData.FirstConnect)
                    {
                        serverData.ID = cO.Data.ID;
                        cO.Data.TcpClient.GetStream().Write(serverData.ToBytes(), 0, serverData.ToBytes().Length);
                    }
                    break;
                case ConnectionType.Hub:
                    Logger.Instance.Log("ConnectionType is [Hub]");
                    break;
            }
            return true;
        }

        List<Data> GetServers(int Offset, int count=6) {
            var index = count * Offset;
            var connectedClients = ConnectedClients
                .Where(cc => cc.Data.ID != -1)
                .Select(cc => cc.Data);

            return connectedClients.Skip(index).Take(count).ToList();
        }

        public void Stop() {
            Logger.Instance.Log("Shutting down ServerHub");
            Listen = false;
            Listener.Stop();
        }
    }
}