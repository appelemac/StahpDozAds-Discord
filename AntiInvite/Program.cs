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

namespace AntiInvite
{
    internal static class GlobalData
    {
        internal static readonly Regex invite = new Regex(@"discord\.gg\/(?<id>[a-zA-Z0-9\-]+)", RegexOptions.Compiled);
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
                try
                {
                    await CommandHandler(s, e);
                    await InviteDeleter(s, e);
                }
                catch (Exception ex)
                {
                    Logger.Log("Oh noes, exception while handling some command: "+ ex);
                }
            };
        
            client.Ready += (s, e) =>
            {
                Console.WriteLine($"[Info] Client Logged in as {client.CurrentUser.Name}");
                Logger.Log("[STARTUP:PreClient] Bot initialized");
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

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Someone pressed CTRL+C, exitting");
            Environment.Exit(0);
        }

        static async Task InviteDeleter(object sender, MessageEventArgs e)
        {
            var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
            if (ServerData.Enabled)
            {
                if (ConfigHandler.Config.Owner != e.User.Id && !ServerData.UsersIgnored.Contains(e.User.Id) && !ServerData.ChannelsIgnored.Contains(e.Channel.Id) && !e.User.ServerPermissions.ManageMessages)
                {
                    Match match = GlobalData.invite.Match(e.Message.Text.Replace("discordapp.com/invite", "discord.gg"));
                    if (!match.Success)
                        return;

                    Invite invitation = await e.Message.Client.GetInvite(match.Groups["id"].Value);

                    if (invitation != null)
                    {
                        Logger.Log("[INVITE:Detected] Invite Detected, " + e.Server.Name + "/" + e.Channel.Name + " - " + e.User.Name + " " + match.ToString());
                        await e.Message.Delete();

                        if (ServerData.IncidentTracker[e.User.Id].MessageCounter%5 == 0)
                        {
                            if (ServerData.IncidentTracker[e.User.Id].ResponseCounter + 1 > ServerData.BanAfter)
                            {
                                await e.Server.Ban(e.User);
                                Logger.Log("[ACTION:Ban] " + e.User.Name + "/" + e.User.Id + " was banned on " + e.Server.Name);
                                return;
                            }
                            if (ServerData.IncidentTracker[e.User.Id].ResponseCounter + 1 > ServerData.KickAfter)
                            {
                                await e.User.Kick();
                                Logger.Log("[ACTION:Kick] " + e.User.Name + "/" + e.User.Id + " was kicked on " + e.Server.Name);
                                return;
                            }
                            await Reply(e, ServerData.Message);
                            ServerData.IncidentTracker[e.User.Id].ResponseCounter++;
                        }
                        ServerData.IncidentTracker[e.User.Id].MessageCounter++;
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
                            if (GlobalData.verboseConsole)
                                GlobalData.verboseConsole = false;
                            else
                                GlobalData.verboseConsole = true;
                            break;
                    }
                }


                if (ConfigHandler.Config.Owner == e.User.Id || e.User.ServerPermissions.ManageRoles)
                {
                    var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
                    switch (commandName)
                    {
                        case "ignore-channel":
                            if (ServerData.ChannelsIgnored.Contains(e.Channel.Id))
                            {
                                ServerData.ChannelsIgnored.Add(e.Channel.Id);
                                await Reply(e, "Channel added to Ignore List!");
                                Logger.Log("[ACTION:Ignore] " + e.Channel.Name + " was ignored on " + e.Server.Name);
                                ConfigHandler.SaveServerData();
                            }
                            else
                                await Reply(e, "This channel is already ignored!");
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
                            Logger.Log("[ACTION:Ignore] Users ignored on " + e.Server.Name + " - " + string.Join(" | ", users));
                            break;
                        case "toggle-monitoring":
                            string Toggle;
                            if (ServerData.Enabled)
                            {
                                ServerData.Enabled = false;
                                Toggle = "OFF";
                            }
                            else
                            {
                                ServerData.Enabled = true;
                                Toggle = "ON";
                            }
                            Logger.Log("[SETTINGS-Server:WatchingToggle] " + e.User.Name + " turned the Invite Monitoring <" + Toggle);
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-message":
                            ServerData.Message = messageSplit.Skip(1).ToString();
                            Logger.Log("[SETTINGS-Server:WarningMessage] " + e.User.Name + " set the warning message on " + e.Server.Name + " to " + messageSplit.Skip(1).ToString());
                            ConfigHandler.SaveServerData();
                            break;
                        case "set-kickafter":
                            short kickAfter;
                            if (short.TryParse(messageSplit[2], out kickAfter))
                            {
                                ServerData.KickAfter = kickAfter;
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
                                ServerData.BanAfter = banAfter;
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
            public static ConcurrentDictionary<ulong, ServerDataBase> ServerData = new ConcurrentDictionary<ulong, ServerDataBase>();

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
                    ServerData = JsonConvert.DeserializeObject<ConcurrentDictionary<ulong, ServerDataBase>>(File.ReadAllText("Settings/serverData.json"));
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
        private static readonly object LoggerLock = new object();

        public static void Log(string logMessage, params object[] args)
        {
            string toBeLogged = $"[{GlobalData.HhMmSs}] {logMessage}";
            using (StreamWriter writer = new StreamWriter(new FileStream(LogsPath(), FileMode.Append)))
            {
                lock (LoggerLock)
                  writer.WriteLine(toBeLogged);
            }
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
