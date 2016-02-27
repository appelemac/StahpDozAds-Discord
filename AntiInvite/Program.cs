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

namespace AntiInvite
{
    internal static class GlobalData
    {
        internal static readonly Regex invite = new Regex(@"discord\.gg\/(?<id>[a-zA-Z0-9\-]+)", RegexOptions.Compiled);
        internal static bool isWatching = false;
        internal static bool verboseConsole = false;
        internal static string HhMmSs => DateTime.UtcNow.ToString("hh:mm:ss");
    }

    class Program
    {
        static string myMention;

        static void Main(string[] args)
        {
            Logger.Log("[STARTUP:Login] Bot Initializing");
            var configBuilder = new DiscordConfigBuilder { LogLevel = LogSeverity.Warning };
            var client = new DiscordClient(configBuilder);

            client.ClientAPI.SendingRequest += (s, e) =>
            {
                var request = e.Request as SendMessageRequest;
                if (request != null)
                {
                    request.Content = request.Content.Replace("@everyone", "@every\x200Bone");
                }
            };

            client.MessageReceived += async (s, e) => await CommandHandler(s, e);
            client.MessageReceived += async (s, e) => await InviteDeleter(s, e);

            client.Ready += (s, e) =>
            {
                Console.WriteLine($"[Info] Client Logged in as {client.CurrentUser.Name}");
                Logger.Log("[STARUP:PreClient] Bot initialized");
                myMention = client.CurrentUser.Mention;
            };
            client.ExecuteAndWait(async () =>
            {
                while (true)
                {
                    try
                    {
                        ConfigHandler.LoadConfig();
                        ConfigHandler.LoadServerData();
                        await client.Connect(ConfigHandler.Config.Email, ConfigHandler.Config.Password);
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            });
        }

        static async Task InviteDeleter(object sender, MessageEventArgs e)
        {
            if (GlobalData.isWatching && ConfigHandler.ServerData[e.Server.Id].Enabled)
            {
                if (ConfigHandler.Config.Owner != e.User.Id && !ConfigHandler.ServerData[e.Server.Id].UsersIgnored.Contains(e.User.Id) && !ConfigHandler.ServerData[e.Server.Id].ChannelsIgnored.Contains(e.Channel.Id) && !e.User.ServerPermissions.ManageMessages)
                {
                    Match match = GlobalData.invite.Match(e.Message.Text.Replace("discordapp.com/invite", "discord.gg"));
                    if (!match.Success)
                        return;

                    Invite invitation = await e.Message.Client.GetInvite(match.Groups["id"].Value);

                    if (invitation != null)
                    {
                        Logger.Log("[INVITE:Detected] Invite Detected, " + e.Server.Name + "/" + e.Channel.Name + " - " + e.User.Name + " " + match.ToString());
                        await e.Message.Delete();

                        if (ConfigHandler.ServerData[e.Server.Id].IncidentTracker[e.User.Id].MessageCounter%5 == 0)
                        {
                            if (ConfigHandler.ServerData[e.Server.Id].IncidentTracker[e.User.Id].ResponseCounter + 1 > ConfigHandler.ServerData[e.Server.Id].BanAfter)
                            {
                                await e.Server.Ban(e.User);
                                Logger.Log("[ACTION:Ban] " + e.User.Name + "/" + e.User.Id + " was banned on " + e.Server.Name);
                                return;
                            }
                            if (ConfigHandler.ServerData[e.Server.Id].IncidentTracker[e.User.Id].ResponseCounter + 1 > ConfigHandler.ServerData[e.Server.Id].KickAfter)
                            {
                                await e.User.Kick();
                                Logger.Log("[ACTION:Kick] " + e.User.Name + "/" + e.User.Id + " was kicked on " + e.Server.Name);
                                return;
                            }
                            await Reply(e, ConfigHandler.ServerData[e.Server.Id].Message);
                            ConfigHandler.ServerData[e.Server.Id].IncidentTracker[e.User.Id].ResponseCounter++;
                        }
                        ConfigHandler.ServerData[e.Server.Id].IncidentTracker[e.User.Id].MessageCounter++;
                    }
                }
            }
        }

        static async Task CommandHandler(object sender, MessageEventArgs e)
        {
            if (e.Message.RawText.StartsWith(myMention))
            {
                var messageSplit = e.Message.RawText.Split(' ');
                if (messageSplit.Length <= 1)
                    return;

                string commandName = messageSplit[1].Trim().ToLower();

                if (ConfigHandler.Config.Owner == e.User.Id)
                {
                    switch (commandName)
                    {
                        case "debug":
                            List<string> usersIgnored = new List<string>();
                            foreach (var u in ConfigHandler.ServerData[e.Server.Id].UsersIgnored)
                            {
                                string username = e.Server.GetUser(u).Name;
                                string user = username + " " + u.ToString();
                                usersIgnored.Add(user);
                            }
                            string response = "__Users Ignored in Server__" + "\n" + String.Join("\n", usersIgnored);
                            if (ConfigHandler.ServerData[e.Server.Id].ChannelsIgnored.Contains(e.Channel.Id))
                                response = "__Channel is ignored__\n\n" + response;

                            string statusOfServerWatching = "";
                            if (ConfigHandler.ServerData[e.Server.Id].Enabled)
                                statusOfServerWatching = "Enabled";
                            else
                                statusOfServerWatching = "Disabled";
                            response = $"*Status of {e.Channel.Name} of {e.Server.Name}\n\n__Monitoring for Invites is **{statusOfServerWatching}** on this server__\n\n" + response;
                            await e.User.SendMessage(response);
                            break;
                        case "verbose":
                            if (GlobalData.verboseConsole)
                                GlobalData.verboseConsole = false;
                            else
                                GlobalData.verboseConsole = true;
                            break;
                    }
                }


                if (ConfigHandler.Config.Owner == e.User.Id || e.Server.Owner == e.User || e.User.ServerPermissions.ManageRoles)
                {
                    switch (commandName)
                    {
                        case "ignore-channel":
                            if (!ConfigHandler.ServerData[e.Server.Id].ChannelsIgnored.Contains(e.Channel.Id))
                            {
                                ConfigHandler.ServerData[e.Server.Id].ChannelsIgnored.Add(e.Channel.Id);
                                await Reply(e, "Channel added to Ignore List!");
                                Logger.Log("[ACTION:Ignore] " + e.Channel.Name + " was ignored on " + e.Server.Name);
                                ConfigHandler.SaveServerData();
                            }
                            else
                                await Reply(e, "This channel is already ignored!");
                            break;
                        case "ignore-users":
                            if (!e.Message.MentionedUsers.Any())
                                await Reply(e, "Please specify, using mentions, users to add to the Ignore List");

                            List<string> responseMessages = new List<string>();
                            List<string> users = new List<string>();
                            foreach (var user in e.Message.MentionedUsers)
                            {
                                if (user.Id != e.Channel.Client.CurrentUser.Id)
                                {
                                    if (ConfigHandler.ServerData[e.Server.Id].UsersIgnored.Contains(user.Id))
                                    {
                                        responseMessages.Add($"{user.Name} was already on the Ignore List!");
                                    }
                                    else
                                    {
                                        ConfigHandler.ServerData[e.Server.Id].UsersIgnored.Add(user.Id);
                                        responseMessages.Add($"{user.Name} was added to the Ignore List!");
                                        users.Add(user.Name);
                                    }
                                }
                            }

                            await Reply(e, " " + string.Join("\n", responseMessages));
                            ConfigHandler.SaveServerData();
                            Logger.Log("[ACTION:Ignore] Users ignored on " + e.Server.Name + " - " + string.Join(" | ", users));
                            break;
                        case "toggle-monitoring":
                            string Toggle;
                            if (ConfigHandler.ServerData[e.Server.Id].Enabled)
                            {
                                ConfigHandler.ServerData[e.Server.Id].Enabled = false;
                                Toggle = "OFF";
                            }
                            else
                            {
                                ConfigHandler.ServerData[e.Server.Id].Enabled = true;
                                Toggle = "ON";
                            }
                            Logger.Log("[SETTINGS-Server:WatchingToggle] " + e.User.Name + " turned the Invite Monitoring <" + Toggle);
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-message":
                            ConfigHandler.ServerData[e.Server.Id].Message = messageSplit.Skip(1).ToString();
                            Logger.Log("[SETTINGS-Server:WarningMessage] " + e.User.Name + " set the warning message on " + e.Server.Name + " to " + messageSplit.Skip(1).ToString());
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-kickafter":
                            short kickAfter;
                            if (short.TryParse(messageSplit[2], out kickAfter))
                            {
                                ConfigHandler.ServerData[e.Server.Id].KickAfter = kickAfter;
                                ConfigHandler.SaveServerData();
                                Logger.Log("[SETTINGS-Server:KickAfter] " + e.User.Name + " set the kickAfter on " + e.Server.Name + " to " + kickAfter);
                                await Reply(e, "Bot will now kick after giving " + kickAfter + "warnings.");
                            }
                            else
                                await Reply(e, "Error!! `" + messageSplit[2] + "` is not a valid parameter! Please give a number.");
                            break;
                        case "set-banafter":
                            short banAfter;
                            if (short.TryParse(messageSplit[2], out banAfter))
                            {
                                ConfigHandler.ServerData[e.Server.Id].BanAfter = banAfter;
                                ConfigHandler.SaveServerData();
                                Logger.Log("[SETTINGS-Server:BanAfter] " + e.User.Name + " set the BanAfter on " + e.Server.Name + " to " + banAfter);
                                await Reply(e, "Bot will now kick after giving " + banAfter + "warnings.");
                            }
                            else
                                await Reply(e, "Error!! `" + messageSplit[2] + "` is not a valid parameter! Please give a number.");
                            break;
                    }
                }
                if (e.User.ServerPermissions.ManageMessages)
                {
                    switch(commandName) {
                        case "clean":
                            Clean(e);
                            break;
                        case "clear":
                            Clean(e);
                            break;
                    }
                }
            }
        }

        static async Task<Message> Reply(MessageEventArgs e, string reply)
        {
            return await e.Channel.SendMessage(e.User.Mention + ", " + reply);
        }

        static class ConfigHandler
        {
            public static ConfigBase Config = new ConfigBase();
            public static Dictionary<ulong, ServerDataBase> ServerData = new Dictionary<ulong, ServerDataBase>();

            static string Time => DateTime.UtcNow.ToString("yy-MM-dd hh:mm:ss");

            public static bool LoadConfig()
            {
                try
                {
                    Config = JsonConvert.DeserializeObject<ConfigBase>(File.ReadAllText("Settings/config.json"));
                    return true;
                }
                catch
                {
                    Console.WriteLine("Error loading auth.json!! Press any key to close...");
                    Logger.Log("[FATAL!!] `auth.json` failed to load");
                    Console.ReadKey();
                    Environment.Exit(0);
                    return false;
                }
            }

            public static bool LoadServerData()
            {
                try
                {
                    ServerData = JsonConvert.DeserializeObject<Dictionary<ulong, ServerDataBase>>(File.ReadAllText("Settings/serverData.json"));
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error - ServerData] {Time}{Environment.NewLine}{ex}");
                    return false;
                }
            }

            public static bool SaveConfig()
            {
                try
                {
                    File.WriteAllText("Settings/config.json", JsonConvert.SerializeObject(Config));
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error - Configs] {Time}{Environment.NewLine}{ex}");
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
                    Console.WriteLine($"[Error - ServerData] {Time}{Environment.NewLine}{ex}");
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

            public class ServerDataBase
            {
                public bool Enabled = false;
                public bool Kick = false;
                public short KickAfter = Config.KickAfter_Default;
                public short BanAfter = Config.BanAfter_Default;
                public string Message = Config.Message;
                public HashSet<ulong> UsersIgnored = new HashSet<ulong>();
                public HashSet<ulong> ChannelsIgnored = new HashSet<ulong>();
                public Dictionary<ulong, IncidentData> IncidentTracker = new Dictionary<ulong, IncidentData>();
            }

            public class IncidentData
            {
                public short ResponseCounter = 0;
                public short MessageCounter = 0;
                public DateTime LastMessage;
            }
        }

        public static async void Clean(MessageEventArgs e)
        {
            Message[] messages = await e.Channel.DownloadMessages(20);

            Array messagesToDelete = messages.Where(o => o.User.Id == e.Server.Client.CurrentUser.Id).ToArray();

            short TotalMsgs = 0;

            foreach(Message m in messagesToDelete)
            {
                await m.Delete();
                TotalMsgs++;
            }
            Logger.Log("[ACTION:Clean] " + TotalMsgs + " were deleted in " + e.Channel.Name + " of " + e.Server.Name + " by " + e.User.Name + "/" + e.User.Id);
        }
    }
    static class Logger
    {
        static StreamWriter LoggerStream = new StreamWriter(new FileStream("Logs\\temp2.txt", FileMode.Append));

        static readonly object LoggerLock = new object();

        public static void Log(string logMessage, params object[] args)
        {
            string ToBeLogged = "[" + GlobalData.HhMmSs + "] " + logMessage;
            lock (LoggerLock)
                LoggerStream.Write(string.Format(ToBeLogged, args));
        }

        private static string BotLogsPaths()
        {
            return Path.Combine("Logs", "temp.txt");
        }
    }
}
