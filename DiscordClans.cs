using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;
using System;
using System.Linq;
using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DiscordClans", "k1lly0u", "0.1.1"), Description("Log ClansReborn events to Discord")]
    class DiscordClans : RustPlugin
    {
        #region Fields
        [DiscordClient]
        private static DiscordClient Client;

        private User Bot;

        private bool ConnectionExists { get; set; } = false;
        #endregion

        #region Oxide Hooks        
        private void OnServerInitialized()
        {
            ValidateSettings();
        }

        private void Unload()
        {
            if (Client != null)
            {
                Discord.CloseClient(Client);
                Client = null;
            }
        }
        #endregion

        #region Functions
        private void ValidateSettings()
        {
            if (string.IsNullOrEmpty(configData.Discord.APIKey))
            {
                PrintError("No API token set in config... Unable to continue!");
                return;
            }

            if (string.IsNullOrEmpty(configData.Discord.BotID))
            {
                PrintError("No bot client ID set in config... Unable to continue!");
                return;
            }

            EstablishConnection();
        }

        private void EstablishConnection()
        {
            Puts("Establishing connection to your Discord server...");

            DiscordSettings settings = new DiscordSettings();
            settings.ApiToken = configData.Discord.APIKey;
            settings.Debugging = configData.Discord.Debug;

            Discord.CreateClient(this, settings);
        }        

        private void InitializeConnection()
        {
            if (Client == null)
            {
                PrintError("Discord client failed to initialize reference. Unable to continue...");
                return;
            }

            if (Client.DiscordServer == null)
            {
                PrintError("Failed to connect to guild. Unable to continue...");
                return;
            }

            if (Client.DiscordServer.unavailable ?? false)
            {
                PrintError("Guild connection unavailable... Trying again in 30 seconds");
                timer.In(30, InitializeConnection);
                return;
            }

            InitializePlugin();
        }

        private void InitializePlugin()
        {
            Puts($"Connection to {Client.DiscordServer.name} established! Verifying existance of bot...");

            foreach (GuildMember member in Client.DiscordServer.members)
            {
                if (member.user.id == configData.Discord.BotID)
                {
                    Bot = member.user;
                    break;
                }
            }

            if (Bot == null)
            {
                PrintError("Can not find the bot with the specified client ID in your Discord server. Check the client ID is correct and you have invited the bot to your server.\nClosing connection to your Discord server...");
                Discord.CloseClient(Client);
            }
            else
            {
                ConnectionExists = true;
                Puts("Bot found! DiscordClans is now active");
            }
        }
        #endregion

        #region Discord Hooks
        private void Discord_Ready(Ext.Discord.DiscordEvents.Ready readyEvent) => timer.In(3, InitializeConnection);
        
        private void DiscordSocket_WebSocketClosed(string reason, ushort code, bool wasClean)
        {
            if (!wasClean)
            {
                PrintWarning("DiscordExt connection closed uncleanly. Reloading plugin to re-initiate connection to Discord");
                timer.In(5, () => Interface.Oxide.ReloadPlugin(this.Title));
            }
        }

        private void DiscordSocket_WebSocketErrored(Exception ex, string message)
        {
            PrintWarning("DiscordExt websocket errored. Reloading plugin to re-initiate connection to Discord");
            timer.In(5, () => Interface.Oxide.ReloadPlugin(this.Title));
        }
        #endregion

        #region API
        private enum MessageType { Create, Invite, InviteReject, InviteWithdrawn, Join, Leave, Kick, Promote, Demote, Disband, AllianceInvite, AllianceInviteReject, AllianceInviteWithdrawn, AllianceAccept, AllianceWithdrawn, TeamChat, ClanChat, AllyChat }

        private void LogMessage(string message, int messageType)
        {
            ConfigData.LogSettings logSettings;

            if (!configData.Log.TryGetValue((MessageType)messageType, out logSettings) || !logSettings.Enabled)
                return;

            Channel channel = logSettings.GetChannel;
            if (channel == null)
                return;

            if (messageType == (int)MessageType.TeamChat) {
                channel.CreateMessage(Client, $":busts_in_silhouette: [Team]{message}");
            }
            else if (messageType == (int)MessageType.ClanChat)
            {
                channel.CreateMessage(Client, $":busts_in_silhouette: [Clan]{message}");
            }
            else if (messageType == (int)MessageType.AllyChat)
            {
                channel.CreateMessage(Client, $":busts_in_silhouette: [Ally]{message}");
            }
            else
            {  
                Embed embed = new Embed
                {
                    title = $"Clan Log - {(MessageType)messageType}",
                    description = message,
                    color = logSettings.GetColor,
                    footer = new Embed.Footer { text = $"{DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()}" }
                };

                channel.CreateMessage(Client, embed);
            }            
        }
        #endregion

        #region Config        
        private ConfigData configData;
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Discord Settings")]
            public DiscordSettings Discord { get; set; }

            [JsonProperty(PropertyName = "Log Settings")]
            public Hash<MessageType, LogSettings> Log { get; set; }

            public class DiscordSettings
            {
                [JsonProperty(PropertyName = "Bot Token")]
                public string APIKey { get; set; }

                [JsonProperty(PropertyName = "Bot Client ID")]
                public string BotID { get; set; }

                [JsonProperty(PropertyName = "Enable Discord debug mode")]
                public bool Debug { get; set; }
            }

            public class LogSettings
            {
                [JsonProperty(PropertyName = "Logs enabled for this message type")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Log Channel Name")]
                public string Channel { get; set; }

                [JsonProperty(PropertyName = "Embed Color (hex)")]
                public string Color { get; set; }

                [JsonIgnore]
                private Channel _channel;

                [JsonIgnore]
                private int _color = 0;
                
                [JsonIgnore]
                public Channel GetChannel
                {
                    get
                    {
                        if (string.IsNullOrEmpty(Channel))
                            return null;

                        if (_channel == null)                        
                            _channel = Client.DiscordServer.channels.Find(x => x.name.Equals(Channel, StringComparison.OrdinalIgnoreCase));

                        return _channel;
                    }
                }

                [JsonIgnore]
                public int GetColor
                {
                    get
                    {
                        if (_color == 0)
                        {
                            if (string.IsNullOrWhiteSpace(Color))
                                Debug.LogError("Null or empty values are not allowed!");

                            if (Color.Length != 6 && Color.Length != 7)
                                Debug.LogError("Color must be 6 or 7 characters in length.");

                            Color = Color.ToUpper();

                            if (Color.Length == 7 && Color[0] != '#')
                                Debug.LogError("7-character colors must begin with #.");
                            else if (Color.Length == 7)
                                Color = Color.Substring(1);

                            if (Color.Any(xc => !HexAlphabet.Contains(xc)))
                                Debug.LogError("Colors must consist of hexadecimal characters only.");

                            _color = int.Parse(Color, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        }

                        return _color;
                    }
                }

                [JsonIgnore]
                private static readonly char[] HexAlphabet = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Discord = new ConfigData.DiscordSettings
                {
                    APIKey = string.Empty,
                    BotID = string.Empty,
                    Debug = false
                },
                Log = new Hash<MessageType, ConfigData.LogSettings>
                {
                    [MessageType.Create] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#e91e63",
                        Enabled = true
                    },
                    [MessageType.Invite] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#9b59b6",
                        Enabled = true
                    },
                    [MessageType.InviteReject] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#71368a",
                        Enabled = true
                    },
                    [MessageType.InviteWithdrawn] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#71368a",
                        Enabled = true
                    },
                    [MessageType.Join] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#3498db",
                        Enabled = true
                    },
                    [MessageType.Leave] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#206694",
                        Enabled = true
                    },
                    [MessageType.Kick] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#992d22",
                        Enabled = true
                    },
                    [MessageType.Promote] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#f1c40f",
                        Enabled = true
                    },
                    [MessageType.Demote] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#c27c0e",
                        Enabled = true
                    },
                    [MessageType.Disband] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#ad1457",
                        Enabled = true
                    },
                    [MessageType.AllianceInvite] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#95a5a6",
                        Enabled = true
                    },
                    [MessageType.AllianceInviteReject] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#95a5a6",
                        Enabled = true
                    },
                    [MessageType.AllianceInviteWithdrawn] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#95a5a6",
                        Enabled = true
                    },
                    [MessageType.AllianceAccept] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#95a5a6",
                        Enabled = true
                    },
                    [MessageType.AllianceWithdrawn] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#95a5a6",
                        Enabled = true
                    },
                    [MessageType.TeamChat] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#607d8b",
                        Enabled = true
                    },
                    [MessageType.ClanChat] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#607d8b",
                        Enabled = true
                    },
                    [MessageType.AllyChat] = new ConfigData.LogSettings
                    {
                        Channel = "clanlog",
                        Color = "#607d8b",
                        Enabled = true
                    }
                },
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (configData.Version < new VersionNumber(0, 1, 1))
            {
                configData.Log[MessageType.TeamChat] = baseConfig.Log[MessageType.TeamChat];
                configData.Log[MessageType.ClanChat] = baseConfig.Log[MessageType.ClanChat];
                configData.Log[MessageType.AllyChat] = baseConfig.Log[MessageType.AllyChat];
            }
            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion
    }
}
