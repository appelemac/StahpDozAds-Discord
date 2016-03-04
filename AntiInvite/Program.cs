using Discord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Discord.API.Client.Rest;
using System.Collections.Concurrent;
using System.Timers;
using System.Diagnostics;
using System.Text;

namespace AntiInvite
{
    internal static class GlobalData
    {
        internal static readonly Regex invite = new Regex(@"(?:discord(?:\.gg|app\.com\/invite)\/(?<id>([\w]{16}|(?:[\w]+-?){3})))", RegexOptions.Compiled);
        internal static readonly Regex args = new Regex(@"(\`(?<args>.*?)\`)", RegexOptions.Compiled);
        internal static bool IsReady = false;
        internal static bool VerboseConsole = false;
        internal static string HhMmSs => DateTime.UtcNow.ToString("hh:mm:ss");
        internal static string HelpMessage = "🚫 StahpDozAds 🚫\nAn AntiAdvertisement bot by <@96642168176807936>(Khionu Terabite)\n\nFull documentation can be found on his GitHub Wiki:\nhttps://github.com/khionu/StahpDozAds-Discord/wiki\n\nIf you need support, you can find Khio on the Discord Bots Server\nhttps://discord.gg/0cDvIgU2voWn4BaD";
    }

    class Program
    {
        static string myMention;

        static void Main(string[] args)
        {
            Logger.Log("~~~~ Bot Starting up ~~~~");
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

            client.Log.Message += LibraryLogging;

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
                    Logger.Log("[HANDLERS] \n" + ex);
                }
            };

            client.JoinedServer += async (s, e) =>
            {
                await e.Server.Owner.SendMessage($"Hi, I just joined your server {e.Server.Name}!\n\nI am a bot designed to prevent people from advertising invites. I have a handful of features to help accomplish this. Say \"Help\" here to get a list of commands and information on how to best use me.\n\nIf you do not activate me, I will leave your server in 3 days. If you want to remove me now, simply run, from where this bot can see it in your server, \"leave\". If you want me to not accept invites to your server ever again, run \"leave-forever\". If you use \"leave-forever\" I will only be able to come back if *you* DM me the invite.\n\nIf you need assistance, you can find my Developer, Khio, in the Discord Bots Server\nhttps://discord.gg/0cDvIgU2voWn4BaD");
            };

            client.LeftServer += (s, e) =>
            {
                Logger.Log($"Bot has been removed from {e.Server.Name}");
            };

            client.Ready += (s, e) =>
            {
                Console.WriteLine($"[Info] Client Logged in as {client.CurrentUser.Name}");
                Logger.Log("[INITIAL ] Bot initialized");
                myMention = client.CurrentUser.Mention;
                client.SetGame(ConfigHandler.config.GameStatus);
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
                        ConfigHandler.LoadGlobalData();
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
            var Every15min = new Timer(15 * 60 * 1000);
            EveryHour.Elapsed += new ElapsedEventHandler((s, e) => Expire(s, e, client));
            Every15min.Elapsed += new ElapsedEventHandler((s, e) => SaveAll(s, e));
        }

        private static void LibraryLogging(object sender, LogMessageEventArgs e)
        {
            string LibraryError = $"[LIBRARY!] [{e.Severity}] {e.Source} {Environment.NewLine} {e.Message}";
            if (e.Exception != null)
                LibraryError += $"{e.Exception}";
            Logger.Log(LibraryError);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Someone pressed CTRL+C, exitting");
            ConfigHandler.SaveConfig();
            ConfigHandler.SaveGlobalData();
            ConfigHandler.SaveServerData();
            Environment.Exit(0);
        }

        static async Task InviteDeleter(object sender, MessageEventArgs e)
        {
            if (e.Server == null)
                return;
            var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
            if (ServerData.Enabled)
            {
                if (GlobalData.invite.IsMatch(e.Message.RawText))
                {
                    if (ServerData.GlobalSubscribed == true && ConfigHandler.GlobalData.GlobalIgnoreList.Contains(e.User.Id))
                        return;
                    if (ServerData.UsersIgnored.Contains(e.User.Id) || ServerData.ChannelsIgnored.Contains(e.Channel.Id) || RolesIgnore(e))
                        return;
                    if (ServerData.WillIgnoreAfter)
                    {
                        TimeSpan IgnoreAfterMinutes = TimeSpan.FromMinutes(ServerData.IgnoreAfterMinutes);
                        if (DateTime.UtcNow - e.User.JoinedAt >= IgnoreAfterMinutes)
                            return;
                    }
                    Logger.Log($"[INCDNT] Invite Detected, {e.Server.Name} / {e.Channel.Name} -- {e.User.Name}");
                    Logger.Log($"  >>>>>  {e.Message.RawText}");
                    var IncidentData = ServerData.IncidentTracker.GetOrAdd(e.User.Id, id => new ConfigHandler.IncidentData());
                    IncidentData.LastMessage = e.Message.Timestamp;
                    await e.Message.Delete();

                    TimeSpan delay = TimeSpan.FromMinutes(ServerData.WarningDelayMinutes);

                    if (IncidentData.MessageCounter % 2 == 0 || DateTime.UtcNow - IncidentData.LastMessage > delay)
                    {
                        if (IncidentData.ResponseCounter + 2 > ServerData.BanAfter)
                        {
                            await e.Server.Ban(e.User);
                            Logger.Log($"[ACTION] {e.User.Name}/{e.User.Id} was autobanned on {e.Server.Name}");
                            return;
                        }
                        if (IncidentData.ResponseCounter + 1 > ServerData.KickAfter && !IncidentData.HasBeenKicked)
                        {
                            IncidentData.HasBeenKicked = true;
                            await e.User.Kick();
                            Logger.Log($"[ACTION] {e.User.Name}/{e.User.Id} was autokicked on {e.Server.Name}");
                            return;
                        }
                        await Reply(e, ServerData.Message);
                        IncidentData.ResponseCounter++;
                    }
                    IncidentData.MessageCounter++;

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
                var BacktickRegex = GlobalData.args.Matches(e.Message.RawText);
                string[] BacktickArgs = new string[BacktickRegex.Count];
                int ArgsCounter = 0;
                if (BacktickRegex.Count > 0)
                {
                    foreach (Match LeBacktickMatch in BacktickRegex)
                    {
                        BacktickArgs[ArgsCounter] = (LeBacktickMatch.Groups["args"].Value);
                        ArgsCounter++;
                    }
                }

                switch (commandName)
                {
                    case "help":
                        await e.User.SendMessage(GlobalData.HelpMessage);
                        break;
                }

                if (ConfigHandler.config.Owner == e.User.Id)
                {
                    ulong serverID = 0;
                    var ServerData = ConfigHandler.ServerData.GetOrAdd(e.Server.Id, id => new ConfigHandler.ServerDataBase());
                    switch (commandName)
                    {
                        case "verbose":
                            if (GlobalData.VerboseConsole)
                                GlobalData.VerboseConsole = false;
                            else
                                GlobalData.VerboseConsole = true;
                            break;
                        case "blacklist":
                            ulong.TryParse(messageSplit[2], out serverID);
                            var reason = string.Join(" ", messageSplit.Skip(2));
                            ConfigHandler.GlobalData.Deadlist[serverID] = reason;
                            Logger.Log($"[DEADLIST] `{serverID}` added to Deadlist. Reason: {reason}");
                            break;
                        case "del-blacklist":
                            ulong.TryParse(messageSplit[2], out serverID);
                            var FormerBlacklistObject = ConfigHandler.GlobalData.Blacklist[serverID];
                            Logger.Log($"[DEADLIST] `{serverID}` removed from Deadlist");
                            break;
                        case "global-ignore":
                            List<string> responseMessages = new List<string>();
                            List<string> users = new List<string>();
                            foreach (var user in e.Message.MentionedUsers)
                            {
                                if (user.Id != e.Channel.Client.CurrentUser.Id)
                                {
                                    if (ConfigHandler.GlobalData.GlobalIgnoreList.Contains(user.Id))
                                    {
                                        responseMessages.Add($"{user.Name} was already on the Global Ignore List!");
                                    }
                                    else
                                    {
                                        ConfigHandler.GlobalData.GlobalIgnoreList.Add(user.Id);
                                        responseMessages.Add($"{user.Name} was added to the Global Ignore List!");
                                        users.Add(user.Name);
                                    }
                                }
                            }

                            await Reply(e, " " + string.Join("\n", responseMessages));
                            Logger.Log($"[GLOBLUTL] Users ignored Globally - " + string.Join(" | ", users));
                            break;
                        case "global-resume":
                            List<string> responseMessages2 = new List<string>();
                            List<string> users2 = new List<string>();
                            foreach (var user in e.Message.MentionedUsers)
                            {
                                if (user.Id != e.Channel.Client.CurrentUser.Id)
                                {
                                    if (ConfigHandler.GlobalData.GlobalIgnoreList.Contains(user.Id))
                                    {
                                        ConfigHandler.GlobalData.GlobalIgnoreList.Remove(user.Id);
                                        responseMessages2.Add($"{user.Name} was removed from the Global Ignore List!");
                                        users2.Add(user.Name);
                                    }
                                    else
                                    {
                                        responseMessages2.Add($"{user.Name} was never on the Global Ignore List!");
                                    }
                                }
                            }
                            await Reply(e, " " + string.Join("\n", responseMessages2));
                            Logger.Log($"[GLOBLUTL] Users resumed Globally - " + string.Join(" | ", users2));
                            break;
                        case "forcesave":
                            ConfigHandler.SaveConfig();
                            ConfigHandler.SaveGlobalData();
                            ConfigHandler.SaveServerData();
                            await Reply(e, "All Bot settings saved");
                            Logger.Log($"[GLOBLUTL] Settings saved!!");
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
                                Logger.Log($"[UTILITY ] `{e.Channel.Name}` was ignored on `{e.Server.Name}`");
                            }
                            else
                                await Reply(e, "This channel is already ignored!");
                            break;
                        case "resume-channel":
                            if (ServerData.ChannelsIgnored.Contains(e.Channel.Id))
                            {
                                ServerData.ChannelsIgnored.Remove(e.Channel.Id);
                                await Reply(e, "Channel removed from Ignore List!");
                                Logger.Log($"[UTILITY ] `{e.Channel.Name}` was resumed on `{e.Server.Name}`");
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
                            Logger.Log($"[UTILITY ] Users ignored on `{e.Server.Name}` - " + string.Join(" | ", users));
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
                            Logger.Log($"[UTILITY ] Users resumed on `{e.Server.Name}` - " + string.Join(" | ", users2));
                            break;
                        case "ignore-roles":
                            int count_IgnoreRoles = 0;
                            foreach (char cIR in e.Message.RawText)
                                if (cIR == '`') count_IgnoreRoles++;
                            if (BacktickRegex.Count == 0 || count_IgnoreRoles % 2 == 1)
                            {
                                await Reply(e, "Please use valid arguments for this command. Valid arguments are Role Names, inbetween backticks, like this: `Bot Master`");
                                return;
                            }
                            else
                            {
                                HashSet<ulong> RolesToBeAdded = new HashSet<ulong>();
                                HashSet<string> RolesToBeAdded_names = new HashSet<string>();
                                HashSet<string> RolesThatFailed = new HashSet<string>();
                                
                                for(int iIR = 0; iIR < BacktickArgs.Count(); iIR++)
                                {
                                    var roles = e.Server.FindRoles(BacktickArgs[iIR], true).ToArray();
                                    if (roles.Length > 1 || roles.Length == 0)
                                        RolesThatFailed.Add(BacktickArgs[iIR]);
                                    else
                                    {
                                        RolesToBeAdded.Add(roles[0].Id);
                                        RolesToBeAdded_names.Add(roles[0].Name);
                                    }
                                }
                                foreach(var rIR in RolesToBeAdded)
                                    ServerData.RolesIgnored.Add(rIR);
                                string response = $"{RolesToBeAdded.Count} roles were added to the ignore list.";
                                if (RolesThatFailed.Any())
                                {
                                    var FailedRoles = string.Join(", ", RolesThatFailed);
                                    response += $" These roles either do not exist, or were not an exclusive match to any roles: `{FailedRoles}`";
                                }
                                await Reply(e, response);
                                Logger.Log($"[UTILITY ] Roles ignored on `{e.Server.Name}` - " + string.Join(" | ", RolesToBeAdded_names));
                            }
                            break;
                        case "resume-roles":
                            int count_ResumeRoles = 0;
                            foreach (char cIR in e.Message.RawText)
                                if (cIR == '`') count_ResumeRoles++;
                            if (BacktickRegex.Count == 0 || count_ResumeRoles % 2 == 1)
                            {
                                await Reply(e, "Please use valid arguments for this command. Valid arguments are Role Names, inbetween backticks, like this: `Bot Master`");
                                return;
                            }
                            else
                            {
                                HashSet<ulong> RolesToBeRemoved = new HashSet<ulong>();
                                HashSet<string> RolesToBeRemoved_names = new HashSet<string>();
                                HashSet<string> RolesThatDidntMatch = new HashSet<string>();

                                for (int iRR = 0; iRR < BacktickArgs.Count(); iRR++)
                                {
                                    var roles = e.Server.FindRoles(BacktickArgs[iRR], true).ToArray();
                                    if (roles.Length > 1 || roles.Length == 0)
                                        RolesThatDidntMatch.Add(BacktickArgs[iRR]);
                                    else
                                    {
                                        RolesToBeRemoved.Add(roles[0].Id);
                                        RolesToBeRemoved_names.Add(roles[0].Name);
                                    }
                                }
                                foreach (var rRR in RolesToBeRemoved)
                                    ServerData.RolesIgnored.Remove(rRR);
                                string response = $"{RolesToBeRemoved.Count} roles were removed from the ignore list.";
                                if (RolesThatDidntMatch.Any())
                                {
                                    var FailedRoles = string.Join(", ", RolesThatDidntMatch);
                                    response += $" These roles either do not exist, or were not an exclusive match to any roles: `{FailedRoles}`";
                                }
                                await Reply(e, response);
                                Logger.Log($"[UTILITY ] Roles resumed on `{e.Server.Name}` - " + string.Join(" | ", RolesToBeRemoved_names));
                            }
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
                                if (!ServerData.InitialEnable)
                                    ServerData.InitialEnable = true;
                            }
                            Logger.Log($"[UTILITY ] `{e.User.Name}` turned the Invite Monitoring <{ToggleWatching}> for `{e.Server.Name}`");
                            await Reply(e, $"Invite Smiting now turned **{ToggleWatching}**, for this server!!");
                            break;
                        case "set-message":
                            ServerData.Message = string.Join(" ", messageSplit.Skip(2));
                            Logger.Log($"[UTILITY ] `{e.User.Name}` set the warning message on `{e.Server.Name}` to `{ServerData.Message}`");
                            await Reply(e, $"Warning Message set to {ServerData.Message.ToString()}");
                            break;
                        case "set-kickafter":
                            short kickAfter;
                            if (short.TryParse(messageSplit[2], out kickAfter))
                            {
                                ServerData.KickAfter = kickAfter;
                                Logger.Log($"[UTILITY ] `{e.User.Name}` set the kickAfter on `{e.Server.Name}` to `{kickAfter}`");
                                await Reply(e, $"Bot will now kick after giving {kickAfter} warnings.");
                            }
                            else
                                await Reply(e, $"Error!! `{messageSplit[2]}` is not a valid parameter! Please give a number.");
                            break;
                        case "set-banafter":
                            short banAfter;
                            if (short.TryParse(messageSplit[2], out banAfter))
                            {
                                ServerData.BanAfter = banAfter;
                                Logger.Log($"[UTILITY ] `{e.User.Name}` set the BanAfter on `{e.Server.Name}` to `{banAfter}`");
                                await Reply(e, $"Bot will now kick after giving {banAfter} warnings.");
                            }
                            else
                                await Reply(e, $"Error!! `{messageSplit[2]}` is not a valid parameter! Please give a number.");
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
                            Logger.Log($"[UTILITY ] `{e.User.Name}` turned the Kick Action <{ToggleKick}> for `{e.Server.Name}`");
                            await Reply(e, $"Kicking is now turned **{ToggleKick}** for this server!!");
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
                            Logger.Log($"[UTILITY ] `{e.User.Name}` turned the Ban Action <{ToggleBan}>");
                            await Reply(e, $"Banning is now turned **{ToggleBan}** for this server!!");
                            break;
                        case "toggle-memberage":
                            string ToggleMemberAge;
                            if (ServerData.WillIgnoreAfter)
                            {
                                ServerData.WillIgnoreAfter = false;
                                ToggleMemberAge = "OFF";
                            }
                            else
                            {
                                ServerData.WillIgnoreAfter = true;
                                ToggleMemberAge = "ON";
                            }
                            Logger.Log($"[UTILITY ] `{e.User.Name}` turned <{ToggleMemberAge}> ignoring regular members.");
                            await Reply(e, $"Ignoring regular members is now turned **{ToggleMemberAge}** for this server!!");
                            break;
                        case "set-delay":
                            double delay;
                            if (double.TryParse(messageSplit[2], out delay))
                            {
                                ServerData.WarningDelayMinutes = delay;
                                Logger.Log($"[UTILITY ] `{e.User.Name}` set the WarningDelay on `{e.Server.Name}` to `{delay}`");
                                await Reply(e, $"The Delay has been set to `{delay}`");
                            }
                            break;
                        case "set-memberage":
                            double MemberAge;
                            if (double.TryParse(messageSplit[2], out MemberAge))
                            {
                                ServerData.IgnoreAfterMinutes = MemberAge;
                                Logger.Log($"[UTILITY ] `{e.User.Name}` set the IgnoreAfter on `{e.Server.Name}` to `{MemberAge}`");
                                await Reply(e, $"Users will now be ignored after they have been members for `{MemberAge} Minutes`");
                            }
                            break;
                        case "toggle-global":
                            string ToggleGlobalSub;
                            if(ServerData.GlobalSubscribed)
                            {
                                ServerData.GlobalSubscribed = false;
                                ToggleGlobalSub = "OFF";
                            }
                            else
                            {
                                ServerData.GlobalSubscribed = true;
                                ToggleGlobalSub = "ON";
                            }
                            Logger.Log($"[UTILITY ] `{e.User.Name}` turned <{ToggleGlobalSub}> the subscription to the Global Ignore List.");
                            await Reply(e, $"The Global Ignore List is now turned **{ToggleGlobalSub}** for this server!!");
                            break;
                    }
                }
                if (e.User.Id == ConfigHandler.config.Owner || e.User.ServerPermissions.ManageMessages)
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
                            await Task.Delay(1000);
                            await e.Server.Leave();
                            Logger.Log($"[SRVSTATE] `{e.Server.Name}` was left on the command of `{e.User.Name}`");
                            break;
                        case "leave-forever":
                            ConfigHandler.GlobalData.Blacklist[e.Server.Id] = e.Server.Owner.Id;
                            await Reply(e, "So long!");
                            await Task.Delay(1000);
                            await e.Server.Leave();
                            Logger.Log($"[SRVSTATE] `{e.Server.Name}` permanently left on the command of `{e.User.Name}`");
                            break;
                        case "debug":
                            var DebugJson = JsonConvert.SerializeObject(ConfigHandler.ServerData[e.Server.Id], Formatting.Indented);
                            var DebugBytes = Encoding.UTF8.GetBytes(DebugJson);
                            using (var stream = new MemoryStream(DebugBytes))
                            {
                                await e.User.SendFile($"{e.Server.Name}_Settings.json", stream);
                            }
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
                string response;
                string match = GlobalData.invite.Match(e.Message.RawText).Value;
                Invite invite = await e.Message.Client.GetInvite(match);
                if (invite == null)
                    return;
                if (e.Channel.Client.Servers.FirstOrDefault(s => s.Id == invite.Server.Id) != null)
                {
                    response = "⚠ I'm already in this server!!";
                }
                if (ConfigHandler.GlobalData.Deadlist.ContainsKey(invite.Server.Id))
                    response = $"⚠ This server is on the DeadList. It will never be joined by this bot again.\nReason: {ConfigHandler.GlobalData.Deadlist[invite.Server.Id]}";
                else if (ConfigHandler.GlobalData.Blacklist.ContainsKey(invite.Server.Id))
                    response = $"⚠ This server was Blacklisted by the Owner.";
                else
                {
                    response = $"I have accepted your invite!!";
                    await invite.Accept();
                }
                await Reply(e, response);
                Logger.Log($"[SRVRJOIN] Bot as joined `{invite.Server.Name}` at `{e.User.Name}`s request");
            }
        }

        static async Task DMCommands(object sender, MessageEventArgs e)
        {
            if (e.Server != null)
                return;
            var messageSplit = e.Message.RawText.Split(' ');
            if (messageSplit[0].ToLower() == "help")
            {
                await e.Channel.SendMessage(GlobalData.HelpMessage);
            }
        }

        static async Task<Message> Reply(MessageEventArgs e, string reply)
        {
            return await e.Channel.SendMessage(e.User.Mention + "," + reply);
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
            Logger.Log($"[UTILITY ] {TotalMsgs} were deleted in `{e.Channel.Name}`/`{e.Server.Name}` by `{e.User.Name}`");
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

        public static void SaveAll(object source, ElapsedEventArgs e)
        {
            if (!GlobalData.IsReady)
                return;
            ConfigHandler.SaveConfig();
            ConfigHandler.SaveGlobalData();
            ConfigHandler.SaveServerData();
            Logger.Log("[GLOBLUTL] AutoSave executed");
        }

        public static bool RolesIgnore(MessageEventArgs e)
        {
            var ServerData = ConfigHandler.ServerData[e.Server.Id];
            bool TheReturn = false;
            foreach (var r in ServerData.RolesIgnored)
            {
                var role = e.Server.GetRole(r);
                if (e.User.HasRole(role))
                {
                    TheReturn = true;
                    break;
                }
            }
            return TheReturn;
        }

        static class ConfigHandler
        {
            public static ConfigBase config = new ConfigBase();
            public static GlobalDataBase GlobalData = new GlobalDataBase();
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
                    Logger.Log("[CRITICAL] `config.json` failed to load");
                    Console.ReadKey();
                    Environment.Exit(0);
                    return false;
                }
            }

            public static bool LoadGlobalData()
            {
                try
                {
                    GlobalData = JsonConvert.DeserializeObject<GlobalDataBase>(File.ReadAllText("Settings/GlobalData.json"));
                    return true;
                }
                catch
                {
                    Logger.Log("[JSONLOAD] `GlobalData.json` failed to load");
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
                    Logger.Log($"[JSONLOAD] `serverData.json` failed to load");
                    return false;
                }
            }

            public static bool SaveConfig()
            {
                try
                {
                    File.WriteAllText("Settings/config.json", JsonConvert.SerializeObject(config, Formatting.Indented));
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] Error saving `config.json` {Environment.NewLine}{ex}");
                    Logger.Log($"[CRITICAL] Error saving `config.json` {Environment.NewLine}{ex}");
                    return false;
                }
            }

            public static bool SaveGlobalData()
            {
                try
                {
                    File.WriteAllText("Settings/GlobalData.json", JsonConvert.SerializeObject(GlobalData, Formatting.Indented));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[JSONSAVE] Error saving `GlobalData.json` {Environment.NewLine}{ex}");
                    return false;
                }
            }

            public static bool SaveServerData()
            {
                try
                {
                    File.WriteAllText("Settings/serverData.json", JsonConvert.SerializeObject(ServerData, Formatting.Indented));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[JSONSAVE] Error saving `serverData.json` {Environment.NewLine}{ex}");
                    return false;
                }
            }

            public class ConfigBase
            {
                public string Email;
                public string Password;
                public ulong Owner;
                public string Message;
                public string GameStatus;
                public short KickAfter_Default;
                public short BanAfter_Default;
                public double WarningDelayMinutes_Default;
                public double IgnoreAfterMinutes_Default;
            }

            public class GlobalDataBase
            {
                public ConcurrentDictionary<ulong, ulong> Blacklist = new ConcurrentDictionary<ulong, ulong>();   // <ServerID, Owner's UserID>
                public ConcurrentDictionary<ulong, string> Deadlist = new ConcurrentDictionary<ulong, string>();  // <ServerID, Reason>
                public HashSet<ulong> GlobalIgnoreList = new HashSet<ulong>();
            }

            public class ServerDataBase
            {
                public bool Enabled = false;
                public bool InitialEnable = false;
                public bool ForeverGone = false;
                public bool GlobalSubscribed = true;
                public bool Kick = false;
                public bool Ban = false;
                public short KickAfter = config.KickAfter_Default;
                public short BanAfter = config.BanAfter_Default;
                public string Message = config.Message;
                public double WarningDelayMinutes = config.WarningDelayMinutes_Default;
                public double IgnoreAfterMinutes = config.IgnoreAfterMinutes_Default;
                public bool WillIgnoreAfter = false;
                public HashSet<ulong> UsersIgnored = new HashSet<ulong>();
                public HashSet<ulong> ChannelsIgnored = new HashSet<ulong>();
                public HashSet<ulong> RolesIgnored = new HashSet<ulong>();
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
