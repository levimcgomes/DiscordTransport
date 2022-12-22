using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DiscordTransport
{

    using Discord;

    [DisallowMultipleComponent]
    public class DiscordManager : levimcgomes.Utils.Singleton<DiscordManager>
    {
        public Discord discord;
        [SerializeField] private long appID;

        public LobbyManager lobbyManager {
            get => discord.GetLobbyManager();
        }
        public UserManager userManager {
            get => discord.GetUserManager();
        }

        private void Awake() {
            // From Mirror.NetworkManager
            if(Application.isPlaying) {
                // Force the object to scene root, in case user made it a child of something
                // in the scene since DDOL is only allowed for scene root objects
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// Initializes a Discord instance
        /// </summary>
        /// <param name="usePTB">If true, DISCORD_INSTANCE_ID will be set to 1, otherwise 0</param>
        /// <returns>True if successfull or already initialized, false if otherwise</returns>
        public bool InitDiscord(bool usePTB) {
            if(!(discord is null)) {
                Debug.Log("Discord already initialized");
                return true;
            }
            System.Environment.SetEnvironmentVariable("DISCORD_INSTANCE_ID", usePTB ? "1" : "0");

            try {
                discord = new Discord(appID, (ulong)CreateFlags.Default);
                if(Mirror.Transport.active is DiscordTransport) {
                    (Mirror.Transport.active as DiscordTransport).CacheDiscordManagers();
                }
                Debug.Log("Discord initialized");
            }
            catch(ResultException r) {
                Debug.LogWarning($"Unable to initialize Discord! Exception caught wih result \"{r.Result}\" and message \"{r.Message}\"");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Logs info about the current lobby
        /// </summary>
        /// <remarks>Info will only be displayed if Mirror is using a DiscordTransport</remarks>
        public void LogLobbyInfo() {
            if(Mirror.Transport.active is DiscordTransport) {
                var transport = Mirror.Transport.active as DiscordTransport;
                var activeLobby = transport.activeLobby;
                if(activeLobby.Id != 0) {
                    bool logged = false;
                    userManager.GetUser(activeLobby.OwnerId, (Result res, ref User user) => {
                        Debug.Log($"Lobby ID {activeLobby.Id} owned by {user.Username} connect with {activeLobby.Id}:{activeLobby.Secret} which should be equal to {lobbyManager.GetLobbyActivitySecret(activeLobby.Id)}");
                        logged = true;
                    });
                    while(!logged) {
                        DiscordManager.Instance.discord.RunCallbacks();
                        System.Threading.Thread.Sleep(100);
                    }
                } else {
                    Debug.Log("Not connected to a lobby");
                }
                
            }
        }

    } 
}
