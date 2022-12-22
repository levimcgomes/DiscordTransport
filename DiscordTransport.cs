using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DiscordTransport
{
    using Discord;
    using Mirror;
    using System;

    public class DiscordTransport : Transport
    {
        #region Fields

        // Discord managers
        private LobbyManager lobbyManager;
        private UserManager userManager;

        public int callbackTimeoutThreshold = 20000;

        public uint serverCapacity = 8;
        public LobbyType lobbyType = LobbyType.Private;
        private const string Scheme = "discord";

        // Mapping between Discord UserID's and Mirror connectionID's
        private BiDictionary<long, int> clients;
        private int currentMemberID = 0;

        public Lobby activeLobby { get; private set; }
        private bool callbacksFinished = false;

        #endregion

        #region Common

        public override bool Available() {
            return true;
        }
        public override void Shutdown() {
            if(ServerActive()) ServerStop();
            if(ClientConnected()) ClientDisconnect();
        }
        public override int GetMaxPacketSize(int channelId = 0) {
            return 1200; // Totally arbitrary number, check https://github.com/Derek-R-S/Discord-Mirror/blob/master/DiscordTransport.cs#L133
        }

        #endregion

        #region Client

        public override void ClientConnect(string address) {
            if(ClientConnected()) {
                Debug.Log("Client already connected");
                return;
            }

            if(address.Split(":").Length != 2) {
                throw new ArgumentException($"Invalid address {address}, use lobbyID:secret", nameof(address));
            }

            lobbyManager.ConnectLobbyWithActivitySecret(address, (Result res, ref Lobby lobby) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to create Lobby: {res}");
                    //OnClientError?.Invoke(TransportError.Unexpected, "Unable to start server");
                    OnClientDisconnected?.Invoke();
                    return;
                }
                Debug.Log("Client connected...");
                activeLobby = lobby;
                lobbyManager.ConnectNetwork(lobby.Id);
                OpenChannels();
                OnClientConnected?.Invoke();
                callbacksFinished = true;
            });

            if(!WaitForCallbacks()) {
                OnClientError?.Invoke(TransportError.Timeout, "Timeout while waiting for Discord callbacks to complete");
            }
        }
        public override void ClientConnect(Uri uri) {
            if(ClientConnected()) {
                Debug.Log("Client already connected");
                return;
            }

            if(uri.Scheme != Scheme) {
                throw new ArgumentException($"Invalid URI {uri}, use {Scheme}://lobbyID/?secret", nameof(uri));
            }
            if(lobbyManager is null || userManager is null) {
                Debug.Log("Trying to start client, but Discord is not initialized");
                return;
            }

            lobbyManager.ConnectLobbyWithActivitySecret($"{uri.Host}:{uri.Query.Replace("?", "")}", (Result res, ref Lobby lobby) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to create Lobby: {res}");
                    //OnClientError?.Invoke(TransportError.Unexpected, "Unable to start server");
                    OnClientDisconnected?.Invoke();
                    return;
                }

                activeLobby = lobby;
                lobbyManager.ConnectNetwork(lobby.Id);
                OpenChannels();
                OnClientConnected?.Invoke();
            });
        }
        public override void ClientDisconnect() {
            if(!ClientConnected()) return;
            lobbyManager.DisconnectNetwork(activeLobby.Id);
            lobbyManager.DisconnectLobby(activeLobby.Id, (Result res) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to disconnect from lobby: {res}");
                    OnClientError?.Invoke(TransportError.Unexpected, "Unable to disconnect from server");
                    return;
                }

                activeLobby = new Lobby();
            });
        }
        public override void ClientSend(ArraySegment<byte> segment, int channelId = 0) {
            try {
                lobbyManager.SendNetworkMessage(activeLobby.Id, activeLobby.OwnerId, (byte)channelId, segment.ToArray());
                OnServerDataSent?.Invoke(clients.GetByFirst(activeLobby.OwnerId), segment, channelId);
            }
            catch(Exception e) {
                OnClientError?.Invoke(TransportError.Unexpected, $"Unable to send from client, Exception {e}");
            }
        }
        public override bool ClientConnected() {
            return activeLobby.Id != 0;
        }

        #endregion

        #region Server

        public override void ServerStart() {
            if(ServerActive()) {
                Debug.Log("Server already started");
                return;
            }
            if(ClientConnected()) {
                Debug.Log("Trying to start server, but client already is started");
                return;
            }
            if(lobbyManager is null || userManager is null) {
                Debug.Log("Trying to start server, but Discord is not initialized");
                return;
            }

            var txn = lobbyManager.GetLobbyCreateTransaction();
            txn.SetType(lobbyType);
            txn.SetCapacity(serverCapacity);

            lobbyManager.CreateLobby(txn, (Result res, ref Lobby lobby) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to create Lobby: {res}");
                    OnServerError?.Invoke(0, TransportError.Unexpected, "Unable to start server");
                    return;
                }

                activeLobby = lobby;
                lobbyManager.ConnectNetwork(lobby.Id);
                OpenChannels();
                callbacksFinished = true;
            });

            clients = new BiDictionary<long, int>();
            currentMemberID = 1;

            if(!WaitForCallbacks()) {
                OnServerError?.Invoke(0, TransportError.Timeout, "Timeout while waiting for Discord callbacks to complete");
            }
        }
        public override void ServerStop() {
            if(!ServerActive()) return;
            lobbyManager.DisconnectNetwork(activeLobby.Id);
            lobbyManager.DisconnectLobby(activeLobby.Id, (Result res) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to disconnect from lobby: {res}");
                    OnServerError?.Invoke(0, TransportError.Unexpected, "Unable to stop server");
                    return;
                }
            });
            lobbyManager.DeleteLobby(activeLobby.Id, (Result res) => {
                if(!(res == Result.Ok)) {
                    Debug.Log($"Something went wrong trying to disconnect from lobby: {res}");
                    OnServerError?.Invoke(0, TransportError.Unexpected, "Unable to stop server");
                    return;
                }

                activeLobby = new Lobby();
            });
        }
        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = 0) {
            try {
                lobbyManager.SendNetworkMessage(activeLobby.Id, clients.GetBySecond(connectionId), (byte)channelId, segment.ToArray());
                OnServerDataSent?.Invoke(connectionId, segment, channelId);
            }
            catch(Exception e) {
                OnServerError?.Invoke(0, TransportError.Unexpected, $"Unable to send from server, Exception {e}");
            }
        }
        public override void ServerDisconnect(int connectionId) {
            try {
                var txn = lobbyManager.GetMemberUpdateTransaction(activeLobby.Id, clients.GetBySecond(connectionId));
                txn.SetMetadata("kicked", "true");
                lobbyManager.UpdateMember(activeLobby.Id, clients.GetBySecond(connectionId), txn, (Result res) => { });
            } catch { }
        }
        public override string ServerGetClientAddress(int connectionId) {
            return clients.GetBySecond(connectionId).ToString();
        }
        public override Uri ServerUri() {
            UriBuilder builder = new UriBuilder();
            builder.Scheme = Scheme;
            builder.Host = activeLobby.Id.ToString();
            builder.Query = activeLobby.Secret.ToString();
            return builder.Uri;
        }
        public override bool ServerActive() {
            return activeLobby.Id == 0 ? false : activeLobby.OwnerId == userManager.GetCurrentUser().Id;
        }

        #endregion

        #region Utils

        public void CacheDiscordManagers() {
            lobbyManager = DiscordManager.Instance.lobbyManager;
            userManager = DiscordManager.Instance.userManager;
            SetupCallbacks();
        }
        private void OpenChannels() {
            lobbyManager.OpenNetworkChannel(activeLobby.Id, Channels.Reliable, true);
            lobbyManager.OpenNetworkChannel(activeLobby.Id, Channels.Unreliable, false);
        }
        private void SetupCallbacks() {
            lobbyManager.OnMemberConnect += OnMemberConnect;
            lobbyManager.OnMemberDisconnect += OnMemberDisconnect;
            lobbyManager.OnLobbyDelete += OnLobbyDelete;
            lobbyManager.OnNetworkMessage += OnNetworkMessage;
            lobbyManager.OnMemberUpdate += OnMemberUpdate;
        }
        private bool WaitForCallbacks() {
            int startTime = System.DateTime.Now.Millisecond;

            while(!callbacksFinished) {
                DiscordManager.Instance.discord.RunCallbacks();
                System.Threading.Thread.Sleep(100);
                if(System.DateTime.Now.Millisecond - startTime > callbackTimeoutThreshold) return false;
            }
            callbacksFinished = false;
            return true;
        }

        #endregion

        #region Callbacks

        private void OnMemberConnect(long lobbyID, long userID) {
            if(ServerActive()) {
                clients.Add(userID, currentMemberID);
                OnServerConnected?.Invoke(currentMemberID);
                currentMemberID++;
            }
            else {
                if(userID == userManager.GetCurrentUser().Id)
                    OnClientConnected?.Invoke();
            }
        }
        private void OnMemberDisconnect(long lobbyID, long userID) {
            if(ServerActive()) {
                OnServerDisconnected?.Invoke(clients.GetByFirst(userID));
                clients.Remove(userID);
            }

            if(activeLobby.OwnerId == userID) {
                ClientDisconnect();
                OnClientDisconnected?.Invoke();
            }
        }
        private void OnMemberUpdate(long lobbyID, long userID) {
            if(userID == userManager.GetCurrentUser().Id) {
                try {
                    if(lobbyManager.GetMemberMetadataValue(lobbyID, userID, "kicked") == "true") {
                        ClientDisconnect();
                    }
                } catch { }
            }
        }
        private void OnNetworkMessage(long lobbyID, long userID, byte channelID, byte[] data) {
            if(ServerActive()) {
                OnServerDataReceived?.Invoke(clients.GetByFirst(userID), new ArraySegment<byte>(data), channelID);
            } else if(ClientConnected()) {
                OnClientDataReceived?.Invoke(new ArraySegment<byte>(data), channelID);
            }
        }
        private void OnLobbyDelete(long lobbyID, uint reason) {
            if(ClientConnected()) {
                OnClientDisconnected?.Invoke();
            }
        }

        #endregion

        #region Update

        public override void ServerLateUpdate() {
            if(ServerActive())
                lobbyManager.FlushNetwork();
        }
        public override void ClientLateUpdate() {
            if(ClientConnected())
                lobbyManager.FlushNetwork();
        }

        #endregion
    }
}
