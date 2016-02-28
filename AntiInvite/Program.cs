using Discord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Discord.API.Client.Rest;
using System.Globalization;
using System.Collections.Concurrent;
using System.Timers;

namespace AntiInvite
{
    internal static class GlobalData
    {
        internal static readonly Regex invite = new Regex(@"discord\.gg\/(?<id>[a-zA-Z0-9\-]+)", RegexOptions.Compiled);
        internal static bool IsReady = false;
        internal static bool VerboseConsole = true;
        internal static string HhMmSs => DateTime.UtcNow.ToString("hh:mm:ss");
    }

    class Program
    {
        static string myMention;

        static void Main(string[] args)
        {
            try
            {
                Logger.Log("[STARTUP:Login] Bot Initializing");
                var configBuilder = new DiscordConfigBuilder { LogLevel = LogSeverity.Warning };
                var client = new DiscordClient(configBuilder);

                Console.CancelKeyPress += Console_CancelKeyPress;

                client.ClientAPI.SendingRequest += (s, e) =>
                {
                    var request = e.Request as SendMessageRequest;
                    if (request != null)
                    {
                        request.Content = request.Content.Replace("@everyone", "@every\x200Bone");
                    }
                };

                client.MessageReceived += async (s, e) =>
                {
                    if (e.User.Id == client.CurrentUser.Id)
                        return;
                    try
                    {
                        await CommandHandler(s, e);
                        await InviteDeleter(s, e);
                        await AcceptDMInvite(s, e);
                        await DMCommands(s, e);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("[MESSAGEHANDLERS] " + ex);
                    }
                };

                client.JoinedServer += async (s, e) =>
                {
                    await e.Server.Owner.SendMessage($"Hi, I just joined your server {e.Server.Name}!\n\nI am a bot designed to prevent people from advertising invites. I have a handful of features to help accomplish this. Say \"Help\" here to get a list of commands and information on how to best use me.\n\nIf you do not activate me, I will leave your server in 3 days. If you want to remove me now, simply run, from where this bot can see it in your server, \"leave\". If you want me to not accept invites to your server ever again, run \"leave-forever\". If you use \"leave-forever\" I will only be able to come back if *you* DM me the invite.\n\nIf you need assistance, you can find my Developer, Khio, in the Discord Bots Server/nhttps://discord.gg/0cDvIgU2voWn4BaD");
                };

                client.Ready += (s, e) =>
                {
                    Console.WriteLine($"[Info] Client Logged in as {client.CurrentUser.Name}");
                    Logger.Log("[STARTUP:PreClient] Bot initialized");
                    myMention = client.CurrentUser.Mention;
                    GlobalData.IsReady = true;
                };
                client.ExecuteAndWait(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            ConfigHandler.LoadConfig();
                            ConfigHandler.LoadServerData();
                            await client.Connect(ConfigHandler.config.Email, ConfigHandler.config.Password);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                });
                var EveryHour = new Timer(60 * 60 * 1000);
                EveryHour.Elapsed += new ElapsedEventHandler((s, e) => Expire(s, e, client));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Someone pressed CTRL+C, exitting");
            Environment.Exit(0);
        }

        static async Task InviteDeleter(object sender, MessageEventArgs e)
        {
            if (e.Server == null)
                return;

            var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
            var IncidentData = ServerData.IncidentTracker.GetOrAdd(e.User.Id, id => new ConfigHandler.IncidentData());
            if (ServerData.Enabled)
            {
                if (ConfigHandler.config.Owner != e.User.Id && !ServerData.UsersIgnored.Contains(e.User.Id) && !ServerData.ChannelsIgnored.Contains(e.Channel.Id) && !e.User.ServerPermissions.ManageMessages)
                {
                    if (e.Message.RawText.Contains("discord.gg") || e.Message.RawText.Contains("discordapp.com/invite"))
                    {
                        Logger.Log("[INVITE:Detected] Invite Detected, " + e.Server.Name + "/" + e.Channel.Name + " - " + e.User.Name);
                        Logger.Log(" >>MSG>> " + e.Message.RawText);
                        await e.Message.Delete();

                        if (IncidentData.MessageCounter % 5 == 0)
                        {
                            if (IncidentData.ResponseCounter + 1 > ServerData.BanAfter)
                            {
                                await e.Server.Ban(e.User);
                                Logger.Log("[ACTION:Ban] " + e.User.Name + "/" + e.User.Id + " was banned on " + e.Server.Name);
                                return;
                            }
                            if (IncidentData.ResponseCounter + 1 > ServerData.KickAfter && !IncidentData.HasBeenKicked)
                            {
                                IncidentData.HasBeenKicked = true;
                                await e.User.Kick();
                                Logger.Log("[ACTION:Kick] " + e.User.Name + "/" + e.User.Id + " was kicked on " + e.Server.Name);
                                return;
                            }
                            await Reply(e, ServerData.Message);
                            IncidentData.ResponseCounter++;
                        }
                        IncidentData.MessageCounter++;
                    }
                }
            }
        }

        static async Task CommandHandler(object sender, MessageEventArgs e)
        {
            if (e.Server == null)
                return;
            if (e.Message.RawText.StartsWith(myMention))
            {
                var messageSplit = e.Message.RawText.Split(' ');
                if (messageSplit.Length <= 1)
                    return;

                string commandName = messageSplit[1].Trim().ToLower();

                if (ConfigHandler.config.Owner == e.User.Id)
                {
                    ulong serverID = 0;
                    var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
                    switch (commandName)
                    {
                        case "debug":
                            List<string> usersIgnored = new List<string>();
                            foreach (var u in ServerData.UsersIgnored)
                            {
                                string username = e.Server.GetUser(u).Name;
                                string user = username + " " + u.ToString();
                                usersIgnored.Add(user);
                            }
                            string response = "__Users Ignored in Server__" + "\n" + String.Join("\n", usersIgnored);
                            if (ServerData.ChannelsIgnored.Contains(e.Channel.Id))
                                response = "__Channel is ignored__\n\n" + response;

                            string statusOfServerWatching = "";
                            if (ServerData.Enabled)
                                statusOfServerWatching = "Enabled";
                            else
                                statusOfServerWatching = "Disabled";
                            response = $"*Status of {e.Channel.Name} of {e.Server.Name}\n\n__Monitoring for Invites is **{statusOfServerWatching}** on this server__\n\n" + response;
                            await e.User.SendMessage(response);
                            break;
                        case "verbose":
                            if (GlobalData.VerboseConsole)
                                GlobalData.VerboseConsole = false;
                            else
                                GlobalData.VerboseConsole = true;
                            break;
                        case "blacklist":
                            ulong.TryParse(messageSplit[2], out serverID);
                            var BlacklistObject = ConfigHandler.ServerBlacklist[serverID];
                            BlacklistObject.Dead = true;
                            BlacklistObject.DeadReason = string.Join(" ", messageSplit.Skip(2));
                            break;
                        case "del-blacklist":
                            ulong.TryParse(messageSplit[2], out serverID);
                            var FormerBlacklistObject = ConfigHandler.ServerBlacklist[serverID];
                            FormerBlacklistObject.Dead = false;
                            FormerBlacklistObject.DeadReason = "";
                            break;
                    }
                }


                if (ConfigHandler.config.Owner == e.User.Id || e.User.ServerPermissions.ManageRoles)
                {
                    var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
                    switch (commandName)
                    {
                        case "ignore-channel":
                            if (!ServerData.ChannelsIgnored.Contains(e.Channel.Id))
                            {
                                ServerData.ChannelsIgnored.Add(e.Channel.Id);
                                await Reply(e, "Channel added to Ignore List!");
                                Logger.Log("[ACTION:Ignore] " + e.Channel.Name + " was ignored on " + e.Server.Name);
                                ConfigHandler.SaveServerData();
                            }
                            else
                                await Reply(e, "This channel is already ignored!");
                            break;
                        case "resume-channel":
                            if (ServerData.ChannelsIgnored.Contains(e.Channel.Id))
                            {
                                ServerData.ChannelsIgnored.Remove(e.Channel.Id);
                                await Reply(e, "Channel removed from Ignore List!");
                                Logger.Log("[ACTION:Ignore] " + e.Channel.Name + " was resumed on " + e.Server.Name);
                                ConfigHandler.SaveServerData();
                            }
                            else
                                await Reply(e, "This channel is not ignored!");
                            break;
                        case "ignore-users":
                            if (!e.Message.MentionedUsers.Any())
                            {
                                await Reply(e, "Please specify, using mentions, users to add to the Ignore List");
                                return;
                            }
                            List<string> responseMessages = new List<string>();
                            List<string> users = new List<string>();
                            foreach (var user in e.Message.MentionedUsers)
                            {
                                if (user.Id != e.Channel.Client.CurrentUser.Id)
                                {
                                    if (ServerData.UsersIgnored.Contains(user.Id))
                                    {
                                        responseMessages.Add($"{user.Name} was already on the Ignore List!");
                                    }
                                    else
                                    {
                                        ServerData.UsersIgnored.Add(user.Id);
                                        responseMessages.Add($"{user.Name} was added to the Ignore List!");
                                        users.Add(user.Name);
                                    }
                                }
                            }

                            await Reply(e, " " + string.Join("\n", responseMessages));
                            ConfigHandler.SaveServerData();
                            Logger.Log($"[ACTION:Ignore] Users ignored on {e.Server.Name} - " + string.Join(" | ", users));
                            break;
                        case "resume-users":
                            if (!e.Message.MentionedUsers.Any())
                            {
                                await Reply(e, "Please specify, using mentions, users to remove from the Ignore List");
                                return;
                            }
                            List<string> responseMessages2 = new List<string>();
                            List<string> users2 = new List<string>();
                            foreach (var user in e.Message.MentionedUsers)
                            {
                                if (user.Id != e.Channel.Client.CurrentUser.Id)
                                {
                                    if (!ServerData.UsersIgnored.Contains(user.Id))
                                    {
                                        responseMessages2.Add($"{user.Name} was not on the Ignore List!");
                                    }
                                    else
                                    {
                                        ServerData.UsersIgnored.Remove(user.Id);
                                        responseMessages2.Add($"{user.Name} was removed from the Ignore List!");
                                        users2.Add(user.Name);
                                    }
                                }
                            }

                            await Reply(e, " " + string.Join("\n", responseMessages2));
                            ConfigHandler.SaveServerData();
                            Logger.Log($"[ACTION:Ignore] Users ignored on {e.Server.Name} - " + string.Join(" | ", users2));
                            break;
                        case "toggle-watching":
                            string ToggleWatching;
                            if (ServerData.Enabled)
                            {
                                ServerData.Enabled = false;
                                ToggleWatching = "OFF";
                            }
                            else
                            {
                                ServerData.Enabled = true;
                                ToggleWatching = "ON";
                            }
                            Logger.Log($"[SETTINGS-Server:WatchingToggle] {e.User.Name} turned the Invite Monitoring <{ToggleWatching}>");
                            await Reply(e, $"Invite Smiting now turned **{ToggleWatching}**, for this server!!");
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-message":
                            ServerData.Message = string.Join(" ", messageSplit.Skip(2));
                            Logger.Log($"[SETTINGS-Server:WarningMessage] {e.User.Name} set the warning message on {e.Server.Name} to {ServerData.Message}");
                            await Reply(e, $"Warning Message set to {ServerData.Message.ToString()}");
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-kickafter":
                            short kickAfter;
                            if (short.TryParse(messageSplit[2], out kickAfter))
                            {
                                ServerData.KickAfter = kickAfter;
                                ConfigHandler.SaveServerData();
                                Logger.Log($"[SETTINGS-Server:KickAfter] {e.User.Name} set the kickAfter on {e.Server.Name} to {kickAfter}");
                                await Reply(e, "Bot will now kick after giving " + kickAfter + " warnings.");
                            }
                            else
                                await Reply(e, $"Error!! `{messageSplit[2]}` is not a valid parameter! Please give a number.");
                            break;
                        case "set-banafter":
                            short banAfter;
                            if (short.TryParse(messageSplit[2], out banAfter))
                            {
                                ServerData.BanAfter = banAfter;
                                ConfigHandler.SaveServerData();
                                Logger.Log("[SETTINGS-Server:BanAfter] " + e.User.Name + " set the BanAfter on " + e.Server.Name + " to " + banAfter);
                                await Reply(e, "Bot will now kick after giving " + banAfter + " warnings.");
                            }
                            else
                                await Reply(e, "Error!! `" + messageSplit[2] + "` is not a valid parameter! Please give a number.");
                            break;
                        case "toggle-kick":
                            string ToggleKick;
                            if (ServerData.Kick)
                            {
                                ServerData.Kick = false;
                                ToggleKick = "OFF";
                            }
                            else
                            {
                                ServerData.Kick = true;
                                ToggleKick = "ON";
                            }
                            Logger.Log($"[SETTINGS-Server:KickToggle] {e.User.Name} turned the Kick Action <{ToggleKick}>");
                            await Reply(e, $"Kicking is now turned **{ToggleKick}**, for this server!!");
                            ConfigHandler.SaveServerData();
                            break;
                        case "toggle-ban":
                            string ToggleBan;
                            if (ServerData.Ban)
                            {
                                ServerData.Ban = false;
                                ToggleBan = "OFF";
                            }
                            else
                            {
                                ServerData.Ban = true;
                                ToggleBan = "ON";
                            }
                            Logger.Log($"[SETTINGS-Server:BanToggle] {e.User.Name} turned the Ban Action <{ToggleBan}>");
                            await Reply(e, $"Banning is now turned **{ToggleBan}**, for this server!!");
                            ConfigHandler.SaveServerData();
                            break;
                    }
                }
                if (e.User.ServerPermissions.ManageMessages)
                {
                    switch (commandName)
                    {
                        case "clean":
                            Clean(e);
                            break;
                        case "clear":
                            Clean(e);
                            break;
                    }
                }
                if (e.User == e.Server.Owner || e.User.Id == ConfigHandler.config.Owner)
                {
                    switch (commandName)
                    {
                        case "leave":
                            await Reply(e, "So long!");
                            await e.Server.Leave();
                            break;
                        case "leave-forever":
                            ConfigHandler.ServerBlacklist[e.Server.Id].OwnerID = e.Server.Owner.Id;
                            await Reply(e, "So long!");
                            await e.Server.Leave();
                            break;
                    }
                }
            }
        }

        static async Task AcceptDMInvite(object sender, MessageEventArgs e)
        {
            if (e.Server != null)
                return;
            if (GlobalData.invite.Match(e.Message.RawText).Success)
            {
                string match = GlobalData.invite.Match(e.Message.RawText).Value;
                Invite invite = await e.Message.Client.GetInvite(match);
                if (invite == null)
                    return;
                if (e.Channel.Client.Servers.FirstOrDefault(s => s.Id == invite.Server.Id) != null)
                {
                    await e.Channel.SendMessage(":warning: I'm already in this server!!");
                    return;
                }
                ConfigHandler.Blacklist BlacklistObject = null;
                if (ConfigHandler.ServerBlacklist.TryGetValue(invite.Server.Id, out BlacklistObject))
                {
                    if (BlacklistObject.Dead)
                    {
                        await e.Channel.SendMessage($":warning: This server has been added to the Blacklist!! Reason: \"{BlacklistObject.DeadReason}\"");
                        return;
                    }
                    if (e.User.Id != ConfigHandler.ServerBlacklist[invite.Server.Id].OwnerID)
                    {
                        await e.Channel.SendMessage(":warning: This server has been added to the Blacklist by the Server Owner!!");
                        return;
                    }
                }
                await invite.Accept();
                await Task.Delay(2000);
                await e.Channel.SendMessage($"I have accepted your invite to the server \"{invite.Server.Name}\"");
            }
        }

        static async Task DMCommands(object sender, MessageEventArgs e)
        {
            if (e.Server != null)
                return;
            var messageSplit = e.Message.RawText.Split(' ');
            if (messageSplit[0].ToLower() == "help")
            {
                await e.Channel.SendMessage("```\nStahpDozAds - the Dev-Hosted instance of Khio's AntiInvite Bot:\nhttps://github.com/khionu/AntiInvite-Discord\n\n    Bot Prefix is to Mention the bot, for example, \"@StahpDozAds ignore - channel\"\n    Warnings are issued every 5 invites per user per server\n\ntoggle-watching\n        Turns on whether the bot watches for invites\nignore-channel\n        Adds the channel that the command was ran in to the Ignore List\nignore-users @Mention\n        Adds all mentioned users to the Ignore List\nresume-channel\n        Removes the channel from the Ignore List\nresume-users @Mention\n        Removes all mentioned users from the Ignore List\nclean\n        Searches through the last 40 messages, and deletes those that belong to the bot (Alias: clear)\nset-message\n        Sets the message the bot uses for a warning\ntoggle-ban\n        Toggles whether the bot tries to ban after so many warnings (default is 8)\ntoggle-kick\n        Toggles whether the bot tries to kick after so many warnings (default is 4)\nset-banafter\n        Sets the number of warnings the bot gives before banning\nset-kickafter\n        Sets the number of warnings the bot gives before kicking\nleave\n        Makes the bot leave the server.\nleave-forever\n        Same as Leave, but the Server Owner must reinvite\n\nIgnore List, Set Message, BanAfter, and KickAfter commands need Manage Roles to be ran (being Owner grants Manage Roles automatically)\nClean needs Manage Messages\nLeave and Leave-Forever need the Server Owner\n\nTo contact the Developer, ping Khio on the Discord Bots server:\nhttps://discord.gg/0cDvIgU2voWn4BaD\n```");
            }
        }

        static async Task<Message> Reply(MessageEventArgs e, string reply)
        {
            return await e.Channel.SendMessage(e.User.Mention + ", " + reply);
        }

        static class ConfigHandler
        {
            public static ConfigBase config = new ConfigBase();
            public static ConcurrentDictionary<ulong, Blacklist> ServerBlacklist = new ConcurrentDictionary<ulong, Blacklist>();
            public static ConcurrentDictionary<ulong, ServerDataBase> ServerData = new ConcurrentDictionary<ulong, ServerDataBase>();

            static string Time => DateTime.UtcNow.ToString("yy-MM-dd hh:mm:ss");

            public static bool LoadConfig()
            {
                try
                {
                    config = JsonConvert.DeserializeObject<ConfigBase>(File.ReadAllText("Settings/config.json"));
                    return true;
                }
                catch
                {
                    Console.WriteLine("Error loading auth.json!! Press any key to close...");
                    Logger.Log("[FATAL!!] `config.json` failed to load");
                    Console.ReadKey();
                    Environment.Exit(0);
                    return false;
                }
            }

            public static bool LoadBlacklist()
            {
                try
                {
                    ServerBlacklist = JsonConvert.DeserializeObject<ConcurrentDictionary<ulong, Blacklist>>(File.ReadAllText("Settings/blacklist.json"));
                    return true;
                }
                catch
                {
                    Logger.Log("[BLACKLIST] `blacklist.json` failed to load");
                    return false;
                }
            }

            public static bool LoadServerData()
            {
                try
                {
                    ServerData = JsonConvert.DeserializeObject<ConcurrentDictionary<ulong, ServerDataBase>>(File.ReadAllText("Settings/serverData.json"));
                    return true;
                }
                catch
                {
                    Logger.Log($"[SERVERDATA] `serverData.json` failed to load");
                    return false;
                }
            }

            public static bool SaveConfig()
            {
                try
                {
                    File.WriteAllText("Settings/config.json", JsonConvert.SerializeObject(config));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[CONFIG]{Environment.NewLine}{ex}");
                    return false;
                }
            }

            public static bool SaveServerBlacklist()
            {
                try
                {
                    File.WriteAllText("Settings/blacklist.json", JsonConvert.SerializeObject(ServerBlacklist));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[BLACKLIST]{Environment.NewLine}{ex}");
                    return false;
                }
            }

            public static bool SaveServerData()
            {
                try
                {
                    File.WriteAllText("Settings/serverData.json", JsonConvert.SerializeObject(ServerData));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[SERVERDATA]{Environment.NewLine}{ex}");
                    return false;
                }
            }

            public class ConfigBase
            {
                public string Email;
                public string Password;
                public ulong Owner;
                public string Message;
                public short KickAfter_Default;
                public short BanAfter_Default;
            }

            public class Blacklist
            {
                public ulong OwnerID;
                public bool Dead = false;
                public string DeadReason;
            }

            public class ServerDataBase
            {
                public bool Enabled = false;
                public bool InitialEnable = false;
                public bool ForeverGone = false;
                public bool Kick = false;
                public bool Ban = false;
                public short KickAfter = config.KickAfter_Default;
                public short BanAfter = config.BanAfter_Default;
                public string Message = config.Message;
                public HashSet<ulong> UsersIgnored = new HashSet<ulong>();
                public HashSet<ulong> ChannelsIgnored = new HashSet<ulong>();
                public ConcurrentDictionary<ulong, IncidentData> IncidentTracker = new ConcurrentDictionary<ulong, IncidentData>();
            }

            public class IncidentData
            {
                public short ResponseCounter = 0;
                public short MessageCounter = 0;
                public bool HasBeenKicked = false;
                public DateTime LastMessage;
            }
        }

        public static async void Clean(MessageEventArgs e)
        {
            Message[] messages = await e.Channel.DownloadMessages(40);

            int TotalMsgs = 0;

            foreach (Message m in messages.Where(o => o.User.Id == o.Server.CurrentUser.Id))
            {
                await m.Delete();
                TotalMsgs++;
            }
            Logger.Log("[ACTION:Clean] " + TotalMsgs + " were deleted in " + e.Channel.Name + " of " + e.Server.Name + " by " + e.User.Name + "/" + e.User.Id);
        }

        public static void Expire(object source, ElapsedEventArgs e, DiscordClient c)
        {
            if (!GlobalData.IsReady)
                return;
            foreach (var s in c.Servers)
            {
                var ServerData = ConfigHandler.ServerData[s.Id];
                if (!ServerData.InitialEnable && s.JoinedAt >= DateTime.Now.AddDays(3))
                    s.Leave();
            }
        }
    }
    static class Logger
    {
        private static readonly object LoggerLock = new object();

        public static void Log(string logMessage, params object[] args)
        {
            string toBeLogged = $"[{GlobalData.HhMmSs}] {logMessage}";
            using (StreamWriter writer = new StreamWriter(new FileStream(LogsPath(), FileMode.Append)))
            {
                lock (LoggerLock)
                  writer.WriteLine(toBeLogged);
            }
            if (GlobalData.VerboseConsole)
                Console.WriteLine(toBeLogged);
        }

        private static string LogsPath()
        {
            const string logsDir = "logs";
            var file = DateTime.UtcNow.Date.ToString("yyyy-MM-dd") + ".txt";
            var path = Path.Combine(logsDir, file);
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);
            return path;
        }
    }
}
