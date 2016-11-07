
using Discord;
using Discord.Commands;
using Discord.Commands.Permissions.Levels;
using Discord.Commands.Permissions.Visibility;
using Discord.Audio;

using AngleSharp;
using AngleSharp.Html;
using AngleSharp.Dom;
using AngleSharp.Css;
using AngleSharp.Xml;
using AngleSharp.XHtml;

using NAudio;
using NAudio.Wave;
using NAudio.CoreAudioApi;

using System;
using System.Configuration;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml.Linq;
using System.Net.Http;
using Newtonsoft.Json;

namespace ModBot
{
    class ModBot
    {
        CommandService commands;
        DiscordClient discord;
        PermissionLevelService permissions;
        AudioService audioservice;

        Dictionary<ulong, Boolean> WasMod = new Dictionary<ulong, bool>();
        Dictionary<ulong, int> RegionCheck = new Dictionary<ulong, int>();

        Dictionary<string, ulong> UserRef = new Dictionary<string, ulong>();
        Dictionary<string, Boolean> IsAsking = new Dictionary<string, bool>();
        Dictionary<string, string> BattlenetTags = new Dictionary<string, string>();
        Dictionary<string, string> UserLastMessage = new Dictionary<string, string>();
        Dictionary<string, int> UserNumberSameMessage = new Dictionary<string, int>();

        Dictionary<string, ulong> ServerPollRegistry = new Dictionary<string, ulong>();
        Dictionary<string, Dictionary<string, int>> RunningPolls = new Dictionary<string, Dictionary<string, int>>();
        Dictionary<string, ulong> PollMaker = new Dictionary<string, ulong>();
        Dictionary<string, List<ulong>> VotedUsers = new Dictionary<string, List<ulong>>();
        List<ulong> UsedSoundBoard = new List<ulong>();
        int stage = 0;
        List<ulong> RequestedInv = new List<ulong>();
        List<ulong> Private = new List<ulong>();
        List<ulong> Newbs = new List<ulong>();

        List<string> PostedTag = new List<string>();
        Regex NetFriendlyID = new Regex(@"([\w]+)#(\d{4,})");
        Regex NoMp3 = new Regex(@"(\w+).mp3");
        Regex Tagid = new Regex(@"([\w]+) {0,}(#\d{4,})");
        Boolean playing = false;
        Boolean UserPromo = true;
        Boolean hourpromo;




        public ModBot()
        {


            //if (!File.Exists("BattlenetTags.xml"))
            //{
            //    SaveDictTagsStringString(BattlenetTags);
            //}
            //BattlenetTags = LoadDictTagsStringString();
            BattlenetTags = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(@".\BattleTags.txt"));

            Console.WriteLine("added dummy to list");
            PostedTag = Load();
            UserPromo = Properties.Settings.Default.Autouser;
            hourpromo = Properties.Settings.Default.hourpromo;
            Console.WriteLine(UserPromo);
            string logpath = @".\log.txt";
            if (!File.Exists(logpath))
            {
                using (StreamWriter chatlog = File.CreateText(logpath));
                Console.WriteLine("No logfile was found, creating logfile");
            }
            if (!File.Exists(@".\BattleTags.txt"))
            {
                using (StreamWriter chatlog = File.CreateText(@".\BattleTags.txt"));
                Console.WriteLine("No battletag file was found, creating battletag file");
            }

            discord = new DiscordClient(x =>
           {
               x.LogLevel = LogSeverity.Info;
               x.LogHandler = Log;

           });
            
            
            discord.UsingCommands(x =>
            {
                x.PrefixChar = '+';
                x.AllowMentionPrefix = true;
                x.HelpMode = HelpMode.Private;
            });

            commands = discord.GetService<CommandService>();
            //permissions = discord.GetService<PermissionLevelService>();
            discord.UsingPermissionLevels(PermissionResolver);


            discord.UserJoined += async (s, e) =>
            {
                var server = e.Server;
                if (UserPromo == true)
                    await e.User.AddRoles(server.FindRoles("User").FirstOrDefault());
                GreetUser(e.User, e.Server);
                GreetMod(e.User, e.Server);
                Newbs.Add(e.User.Id);
                //await e.User.SendMessage($"Hey {e.User} and welcome to PanzerDiscord! Please tell me, do you play Overwatch? \nValid answers: \n```Yes \nNo \n```".Replace("\n", Environment.NewLine));
                //UserRef.Add(e.User.Name, e.User);
                //Console.WriteLine($"{e.User} has entered the server, overwatch region determination in progress");

            };

            discord.MessageDeleted += async (s, e) =>
            {
                if (e.Channel == GetGamertags(e.Server))
                {
                    PostedTag.Remove(e.Message.User.Id.ToString());
                    BattlenetTags.Remove(e.Message.User.Id.ToString());
                    Save(PostedTag);
                    File.WriteAllText(@".\BattleTags.txt", JsonConvert.SerializeObject(BattlenetTags));
                }
            };

            discord.MessageReceived += async (s, e) =>
            {
                var server = e.Server;
                Channel botlog = null;
                Channel general = null;
                Channel gamertags = null;
                if (!e.Channel.IsPrivate)
                {
                    botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    general = e.Server.FindChannels("#general").FirstOrDefault();
                    gamertags = e.Server.FindChannels("#gamertags").FirstOrDefault();


                    if (e.Channel == general)
                    {
                        using (StreamWriter chatlog = File.AppendText(logpath))
                        {
                            chatlog.WriteLine($"[{DateTime.Now.ToString()}] [{e.User}] : '{e.Message.Text}'");
                        }
                    }
                    if (!e.Message.IsAuthor && !e.User.HasRole(e.Server.FindRoles("BOT").FirstOrDefault()))
                    {

                        if (Newbs.Contains(e.User.Id))
                            Verify(e.User, e.Server);

                        if (e.User.HasRole(GetPunished(e.Server)))
                            await e.Message.Delete();

                        if (e.Channel == gamertags)
                        {
                            if (!BattlenetTags.ContainsKey(e.User.Id.ToString()))
                            { 
                                string realtag = null;
                                var ID = Tagid.Match(e.Message.Text);
                                realtag = $"{ID.Groups[1].Value}{ID.Groups[2].Value}";
                                BattlenetTags.Add(e.User.Id.ToString(), realtag);
                                File.WriteAllText(@".\BattleTags.txt", JsonConvert.SerializeObject(BattlenetTags));
                                Console.WriteLine("Battle.net ID found and archived");
                                var region = await GetRegion(realtag);
                                if (region == "EU")
                                {
                                    await e.User.AddRoles(getEU(e.Server));
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that the region you play overwatch in is {region}. If this is incorrect, please use the +overwatch command in #general.");
                                }
                                else if (region == "NA")
                                {
                                    await e.User.AddRoles(getNA(e.Server));
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that the region you play overwatch in is {region}. If this is incorrect, please use the +overwatch command in #general.");
                                }
                                else if (region == "EU, NA")
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that you have an Overwatch profile on both the NA and EU servers. Please use the +overwatch command in #general to choose your preferred region!");

                            }
                        }


                        if (!e.User.HasRole(GetMod(server)) && !e.User.HasRole(GetAdmin(server)))
                        {

                            if (e.Channel == gamertags)
                            {
                                if (PostedTag.Contains(e.User.Id.ToString()))
                                {
                                    Console.WriteLine($"{e.User.Name} is on PostedTag. Deleting message...");
                                    await e.Message.Delete();
                                    var msg = await e.Channel.SendMessage($"Hey {e.User.Mention}, it seems like you posted in here before. You may only post your gamertag once. If you believe this to be a mistake, please contact an admin.");
                                    await Task.Delay(10000);
                                    await msg.Delete();
                                }
                                else if (!PostedTag.Contains(e.User.Id.ToString()))
                                {
                                    PostedTag.Add(e.User.Id.ToString());
                                    Console.WriteLine($"added {e.User} to the PostedTag list");
                                    Save(PostedTag);
                                    Console.WriteLine($"saved PostedTag");
                                }
                            }

                            if (!(e.Channel == gamertags))
                            {
                                if (Tagid.IsMatch(e.Message.Text))
                                {
                                    await e.Message.Delete();
                                    Message msg = await e.Channel.SendMessage($"Hey {e.User.Mention}! Please post your Battle.net ID into {gamertags.Mention}. If you believe this to be a mistake, please contact an admin.");
                                    Console.WriteLine($"{e.User} has tried to write his ID into a different channel that #gamertags, the message was deleted");
                                    await Task.Delay(2000);
                                    Message msg2 = await gamertags.SendMessage($"Hey {e.User.Mention}! Over here!");
                                    await Task.Delay(8000);
                                    await msg2.Delete();
                                    await msg.Delete();
                                }
                            }


                            if (e.Message.Text.Contains("discord.gg") || e.Message.Text.Contains("discord.com/invite") || e.Message.Text.Contains("discord.me"))
                            {
                                await e.Message.Delete();
                                await e.Channel.SendMessage($"Hey {e.User}, your message has been deleted under the suspicion of it being a server invite!");
                                string report = $"**Action:** Delete invite\n**Mod/Admin:** ModBot\n**Target:** {e.User}".Replace("\n", Environment.NewLine);
                                await botlog.SendMessage(report);
                            }
                            if (e.Message.MentionedUsers.Count() > 5)
                            {
                                await e.Message.Delete();
                                await e.Channel.SendMessage($"Hey {e.User.Mention}, your message has been deleted because it contained too many mentions!");
                                string report = $"**Action:** Delete mass-mention\n**Mod/Admin:** ModBot\n**Target:** {e.User}".Replace("\n", Environment.NewLine);
                                await botlog.SendMessage(report);
                            }
                            if ((e.Message.MentionedUsers.Count() > 10) || (e.Message.Text.Contains("@everyone @everyone @everyone")))
                            {
                                await e.Message.Delete();
                                await e.Channel.SendMessage($"{e.User} has been kicked from the server because his/her message contained too many mentions! :boot:");
                                await e.User.SendMessage("You have been kicked from the PanzerDiscord server because your message contained too many mentions. You may rejoin any time you want.");
                                await Task.Delay(1000);
                                await e.User.Kick();
                                string report = $"**Action:** Mass-mention auto-kick\n**Mod/Admin:** ModBot\n**Target:** {e.User}".Replace("\n", Environment.NewLine);
                                await botlog.SendMessage(report);
                            }
                            if ((e.Message.Text.StartsWith("!play", true, null) || e.Message.Text.StartsWith("!add", true, null) || e.Message.Text.StartsWith("!stop", true, null) || e.Message.Text.StartsWith("!join", true, null) || e.Message.Text.StartsWith("!leave", true, null) || e.Message.Text.StartsWith("!next", true, null) || e.Message.Text.StartsWith("!playlist", true, null)) && !e.Channel.Equals(e.Server.FindChannels("#botmusic").FirstOrDefault()))
                            {
                                await e.Message.Delete();
                                Message msg = await e.Channel.SendMessage($"Hey {e.User}, your message has been deleted because it does not belong into this channel! Please only use the music commands in the #botmusic channel!");
                                string report = $"**Action:** Delete music bot command in wrong channel\n**Mod/Admin:** ModBot\n**Target:** {e.User}".Replace("\n", Environment.NewLine);
                                await botlog.SendMessage(report);
                                await Task.Delay(7000);
                                await msg.Delete();
                            }
                            if (e.Channel == GetGeneral(e.Server))
                            {
                                string lastmessage = null;
                                UserLastMessage.TryGetValue(e.User.Name, out lastmessage);
                                if (e.Message.Text == lastmessage)
                                {
                                    if (UserNumberSameMessage.ContainsKey(e.User.Name))
                                    {
                                        int number = 0;
                                        UserNumberSameMessage.TryGetValue(e.User.Name, out number);
                                        UserNumberSameMessage.Remove(e.User.Name);
                                        UserNumberSameMessage.Add(e.User.Name, number + 1);
                                    }
                                    else
                                    {
                                        UserNumberSameMessage.Add(e.User.Name, 1);
                                    }
                                }
                                else if (e.Message.Text != lastmessage)
                                {
                                    UserNumberSameMessage.Remove(e.User.Name);
                                    UserLastMessage.Remove(e.User.Name);
                                    UserLastMessage.Add(e.User.Name, e.Message.Text);
                                }
                                int amount = 0;
                                UserNumberSameMessage.TryGetValue(e.User.Name, out amount);
                                if (amount >= 3)
                                {
                                    Punish(e.User, e.Server);
                                    UserLastMessage.Remove(e.User.Name);
                                    UserNumberSameMessage.Remove(e.User.Name);
                                    await botlog.SendMessage($"{GetAdmin(e.Server).Mention} {GetMod(e.Server).Mention} {e.User} Has been banned from the server for 1 minute because he repeated the same message 4 times!");
                                    await e.Channel.SendMessage($"{e.User.Name} has been banned from the server for 1 minute because of suspected spam!");
                                    await e.User.SendMessage("You have been banned from the server for 1 minute because I identified your messages as spam. If this is incorrect, please join and inform Sqeed!");
                                    await Task.Delay(1000);
                                    await e.Server.Ban(e.User, 1);
                                    await Task.Delay(60000);
                                    await e.Server.Unban(e.User);
                                }
                            }
                        }

                    }
                }
                if (e.Channel.IsPrivate && !e.Message.IsAuthor)
                {
                    //Console.WriteLine("TEST1");
                    //Server PMServer = null;
                    //UserRef.TryGetValue(e.User.Name, out PMServer);
                    //User PMUser = PMServer.FindUsers(e.User.Name).FirstOrDefault();
                    //RegionCheck.Add(PMUser, 1);
                    string checkingname = e.User.Name;
                    ulong askingname3;
                    UserRef.TryGetValue(e.User.Name, out askingname3);
                    User askingname = e.Server.GetUser(askingname3);
                    if (askingname != null)
                    {
                        string askingname2 = askingname.Name;
                        if (checkingname.Contains(askingname2) && !RegionCheck.ContainsKey(e.User.Id))
                        {
                            Console.WriteLine("added user");
                            RegionCheck.Add(e.User.Id, 1);
                        }
                        else
                            Console.WriteLine("derp");



                        //if (!RegionCheck.ContainsKey(e.User) && !e.User.IsBot)

                        RegionCheck.TryGetValue(e.User.Id, out stage);
                        if (stage == 1)
                        {
                            if (e.Message.Text == "Yes")
                            {
                                stage = 0;
                                await e.User.SendMessage("Good, now please tell me which region you play on: \nValid answers: \n```1 = Americas \n2 = Europe \n3 = Asia```".Replace("\n", Environment.NewLine));
                                RegionCheck.Remove(e.User.Id);
                                RegionCheck.Add(e.User.Id, 2);
                            }
                            else if (e.Message.Text == "No")
                            {
                                stage = 0;
                                await e.User.SendMessage("Okay!");
                                RegionCheck.Remove(e.User.Id);
                                UserRef.Remove(e.User.Name);
                                askingname = null;
                                askingname2 = null;
                                checkingname = null;
                            }
                            else
                            {
                                await e.User.SendMessage("The answer you entered is not a valid answer! Please note that the answers are case sensitive!");
                            }
                        }
                        if (stage == 2)
                        {
                            if (e.Message.Text == "1")
                            {
                                stage = 0;
                                Server OWserver = discord.FindServers("PanzerDiscord").FirstOrDefault();
                                User overwatch = OWserver.FindUsers(e.User.Name).FirstOrDefault();
                                await overwatch.AddRoles(getNA(OWserver));
                                await e.User.SendMessage("Thank you for your time!");
                                RegionCheck.Remove(e.User.Id);
                                UserRef.Remove(e.User.Name);
                                askingname = null;
                                askingname2 = null;
                                checkingname = null;
                            }
                            else if (e.Message.Text == "2")
                            {
                                stage = 0;
                                Server OWserver = discord.FindServers("PanzerDiscord").FirstOrDefault();
                                User overwatch = OWserver.FindUsers(e.User.Name).FirstOrDefault();
                                await overwatch.AddRoles(getEU(OWserver));
                                await e.User.SendMessage("Thank you for your time!");
                                RegionCheck.Remove(e.User.Id);
                                UserRef.Remove(e.User.Name);
                                askingname = null;
                                askingname2 = null;
                                checkingname = null;
                            }
                            else if (e.Message.Text == "3")
                            {
                                stage = 0;
                                Server OWserver = discord.FindServers("PanzerDiscord").FirstOrDefault();
                                User overwatch = OWserver.FindUsers(e.User.Name).FirstOrDefault();
                                await overwatch.AddRoles(getAsia(OWserver)); await e.User.SendMessage("Thank you for your time!");
                                RegionCheck.Remove(e.User.Id);
                                UserRef.Remove(e.User.Name);
                                askingname = null;
                                askingname2 = null;
                                checkingname = null;
                            }
                            else
                            {
                                await e.User.SendMessage("The answer you entered is not a valid answer! Please note that the answers are case sensitive!");
                            }
                        }

                    }
                }

            };

            discord.MessageUpdated += async (s, e) =>
            {
                if (!(e.User == null))
                {
                    if (!e.User.IsBot)
                    {
                        Channel botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                        Channel general = e.Server.FindChannels("#general").FirstOrDefault();
                        Channel gamertags = e.Server.FindChannels("#gamertags").FirstOrDefault();
                        if (e.Channel == gamertags)
                        {
                                string realtag = null;
                                var ID = Tagid.Match(e.After.Text);
                                realtag = $"{ID.Groups[1].Value}{ID.Groups[2].Value}";
                            BattlenetTags.Add(e.User.Id.ToString(), realtag);
                            File.WriteAllText(@".\BattleTags.txt", JsonConvert.SerializeObject(BattlenetTags));
                            Console.WriteLine("Battle.net ID found and archived");
                                var region = await GetRegion(realtag);
                                if (region == "EU")
                                {
                                    await e.User.AddRoles(getEU(e.Server));
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that the region you play overwatch in is {region}. If this is incorrect, please use the +overwatch command in #general.");
                                }
                                else if (region == "NA")
                                {
                                    await e.User.AddRoles(getNA(e.Server));
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that the region you play overwatch in is {region}. If this is incorrect, please use the +overwatch command in #general.");
                                }
                                else if (region == "EU, NA")
                                    await e.User.SendMessage($"Hey {e.User.Name}! I have detected that you have an Overwatch profile on both the NA and EU servers. Please use the +overwatch command in #general to choose your preferred region!");
                            
                        
                    
                    }
                    }
                }
            };

            //commands.CommandErrored += async (s, e) =>
            //{
            //    if (e.Command == null)
            //        await e.Channel.SendMessage("Bad syntax!");
            //    else
            //    {
            //        Console.WriteLine($"THE COMMAND {e.Command.Text} HAS ERRORED!");
            //        await e.Channel.SendMessage($"Hey {e.User.Mention}! The command {e.Command.Text} didn't execute properly... {GetAdmin(e.Server).Mention}");
            //    }
            //};

            discord.UserLeft += async (s, e) =>
            {
                ByeUser(e.User, e.Server);
                BattlenetTags.Remove(e.User.Id.ToString());
                File.WriteAllText(@".\BattleTags.txt", JsonConvert.SerializeObject(BattlenetTags));
                PostedTag.Remove(e.User.Id.ToString());
                Save(PostedTag);
                List<Message> Targets = new List<Message>();
                User u = e.User;
                var messages = await GetGamertags(e.Server).DownloadMessages(100);
                List<Message> MessageTemp = messages.ToList();
                foreach (Message message in MessageTemp)
                {
                    if (message.User == u)
                        Targets.Add(message);
                }
                messages = Targets.ToArray();
                
                await GetGamertags(e.Server).DeleteMessages(messages);
                if (Newbs.Contains(e.User.Id))
                    Newbs.Remove(e.User.Id);
                

            };


            discord.UserUpdated += async (s, e) =>
            {
                if (Private.Contains(e.After.Id) && (e.After.VoiceChannel != null))
                {
                    await e.After.Edit(null, null, e.Server.GetChannel(233698229005451264));
                    Private.Remove(e.After.Id);
                }
            };




            RegisterPINGPONG();
            RegisterBan();
            RegisterKick();
            RegisterVoiceMute();
            RegisterUnvoicemute();
            RegisterPermaBan();
            RegisterChatMute();
            RegisterChatunMute();
            RegisterPunish();
            RegisterUnpunish();
            //RegisterHelp();
            RegisterAutoUser();
            RegisterOverwatch();
            RegisterMVP();
            RegisterMod();
            RegisterGetID();
            RegisterGreetMessage();
            RegisterGreetMod();
            RegisterGreetUserEnable();
            RegisterLeaveEnable();
            RegisterLeaveMessage();
            RegisterMakePoll();
            RegisterViewPoll();
            RegisterClosePoll();
            RegisterVote();
            RegisterStop();
            RegisterPlay();
            RegisterClear();
            RegisterApproveInvite();

            discord.UsingAudio(x => // Opens an AudioConfigBuilder so we can configure our AudioService
            {
                x.Mode = AudioMode.Outgoing; // Tells the AudioService that we will only be sending audio
            });

            audioservice = discord.GetService<AudioService>();

            discord.ExecuteAndWait(async () =>
            {
                await discord.Connect("", TokenType.Bot);
            });





            //PostedTag.Add(((discord.FindServers("PanzerDiscord").FirstOrDefault()).FindUsers("SqeedDummy").FirstOrDefault()).Name);
            //Save(PostedTag);
        }


        //LIST LOADER
        private List<string> Load()
        {
            string file = "TaggedList.xml";
            List<string> listofa = new List<string>();
            XmlSerializer formatter = new XmlSerializer(typeof(List<string>));
            FileStream aFile = new FileStream(file, FileMode.Open);
            byte[] buffer = new byte[aFile.Length];
            aFile.Read(buffer, 0, (int)aFile.Length);
            MemoryStream stream = new MemoryStream(buffer);
            return (List<string>)formatter.Deserialize(stream);
        }

        //LIST SAVER
        private void Save(List<string> TaggedList)
        {
            string path = "TaggedList.xml";
            FileStream outFile = File.Create(path);
            XmlSerializer formatter = new XmlSerializer(typeof(List<string>));
            formatter.Serialize(outFile, TaggedList);
            outFile.Close();
        }

        //DICTSRINGSTRING LOADER
        private Dictionary<string, string> LoadDictTagsStringString()
        {
            string file = $"BattlenetTags.xml";
            XmlSerializer formatter = new XmlSerializer(typeof(XElement));
            FileStream aFile = new FileStream(file, FileMode.Open);
            byte[] buffer = new byte[aFile.Length];
            aFile.Read(buffer, 0, (int)aFile.Length);
            MemoryStream stream = new MemoryStream(buffer);
            XElement rootElement = XElement.Load(stream);
            Dictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var el in rootElement.Elements())
            {
                dict.Add(el.Name.LocalName, el.Value);
            }
            return dict;
            //return (Dictionary<string, string>)formatter.Deserialize(dict);
        }

        //DICTSTRINGSTRING SAVER
        private void SaveDictTagsStringString(Dictionary<int, string> TaggedList)
        {

            //string path = $"BattlenetTags.xml";
            //XElement el = new XElement("root",
            //    TaggedList.Select(kv => new XElement(kv.Key, kv.Value)));
            //FileStream outFile = File.Create(path);
            //XmlSerializer formatter = new XmlSerializer(typeof(XElement));
            //formatter.Serialize(outFile, el);
            //outFile.Close();
        }

        //GET FUNCTIONS
        private Discord.Role getEU(Server Y)
        {
            return Y.FindRoles("EU").FirstOrDefault();
        }

        private Discord.Role getNA(Server Y)
        {
            return Y.FindRoles("NA").FirstOrDefault();
        }

        private Discord.Role getAsia(Server Y)
        {
            return Y.FindRoles("Asia").FirstOrDefault();
        }

        private User getbot(Server Y)
        {
            return Y.FindUsers("SqeedDummy").FirstOrDefault();
        }

        private Discord.Role GetMuted(Server Y)
        {
            var server = Y;
            return server.FindRoles("Muted").FirstOrDefault();
        }

        private Discord.Role GetPunished(Server Y)
        {
            var server = Y;
            return server.FindRoles("PUNISHED").FirstOrDefault();
        }

        private Discord.Role GetMod(Server Y)
        {
            var server = Y;
            return server.FindRoles("Moderator").FirstOrDefault();
        }

        private Discord.Role GetAdmin(Server Y)
        {
            var server = Y;
            return server.FindRoles("Admin").FirstOrDefault();

        }

        private Discord.Role GetMVP(Server Y)
        {
            return Y.FindRoles("MVPs").FirstOrDefault();
        }

        private Channel GetRules(Server Y)
        {
            return Y.FindChannels("#rules").FirstOrDefault();
        }

        private Channel GetGeneral(Server Y)
        {
            return Y.FindChannels("#general").FirstOrDefault();
        }

        private Channel GetLobby(Server Y)
        {
            return Y.FindChannels("💬 Lobby").FirstOrDefault();
        }

        private Server GetPD()
        {
            return discord.FindServers("PanzerDiscord").FirstOrDefault();
        }

        private Discord.Role GetMember(Server Y)
        {
            return Y.FindRoles("User").FirstOrDefault();
        }

        private Channel GetGamertags(Server Y)
        {
            return Y.FindChannels("#gamertags").FirstOrDefault();
        }

        protected virtual bool IsFileinUse(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }
            return false;
        }
        //END GET FUNCTIONS

        //JOB FUNCTIONS
        private async void unPunish(User X, Server Y)
        {
            var server = Y;
            await X.RemoveRoles(server.FindRoles("PUNISHED").FirstOrDefault());
        }

        private async void Punish(User X, Server Y)
        {

            var server = Y;
            await X.AddRoles(GetPunished(Y));
            await X.RemoveRoles(Y.FindRoles("Moderator").FirstOrDefault());

        }

        private async void GreetMod(User x, Server Y)
        {
            if (Properties.Settings.Default.GreetMod == true)
            {
                var server = Y;
                Channel moderation = server.FindChannels("#moderation").FirstOrDefault();
                Discord.Role mod = server.FindRoles("Moderator").FirstOrDefault();
                Discord.Role admin = server.FindRoles("Admin").FirstOrDefault();
                //if (Convert.ToInt64(x.Id) != 231775385703022593)
                    await moderation.SendMessage($"{mod.Mention} {admin.Mention} A new user called {x} has just joined the server! Go greet him!");
            }
        }

        //START VERIFICATION
        private async void Verify(User X, Server Y)
        {
            if (UserPromo == true)
            {
                Discord.Role User = Y.FindRoles("User").FirstOrDefault();
                await Task.Delay(1800000);
                if (Newbs.Contains(X.Id))
                {
                    await X.AddRoles(User);
                    Newbs.Remove(X.Id);
                }
            }
        }

        //GETREGION
        private async Task<string> GetRegion(string X)
        {
            Console.WriteLine("GetRegion called");
            string RealID = null;
            var ID = NetFriendlyID.Match(X);
            RealID = $"{ID.Groups[1].Value}-{ID.Groups[2].Value}";
            string baseUrl = "http://playoverwatch.com/en-gb/career/";
            string naAppend = $"pc/us/{RealID}";
            string euAppend = $"pc/eu/{RealID}";
            Boolean EU = false;
            Boolean NA = false;
            string region = null;
            HttpClient _client = new HttpClient();
            Console.WriteLine("Created HTTP Client");
            _client.BaseAddress = new Uri(baseUrl);
            Console.WriteLine("Checking NA response");
            var responseNA = await _client.GetAsync(naAppend);

            if (responseNA.IsSuccessStatusCode)
            {
                NA = true;
                Console.WriteLine("NA positive");
            }
            Console.WriteLine("Checking EU response");
            var responseEU = await _client.GetAsync(euAppend);

            if (responseEU.IsSuccessStatusCode)
            {
                EU = true;
                Console.WriteLine("EU positive");
            }
            if (EU == true)
                region = "EU";
            if (NA == true)
                region = "NA";
            if (NA == true && EU == true)
                region = $"EU, NA";
            Console.WriteLine($"EU: {EU}");
            Console.WriteLine($"EU: {NA}");
            Console.WriteLine($"Region: {region}");
            return region;
        }

        //GETRANK
        public async Task<int> GetRank(string X)
        {
            var ID = NetFriendlyID.Match(X);
            string RealID = $"{ID.Groups[1].Value}-{ID.Groups[2].Value}";
            string URL = null;
            string baseUrl = "http://playoverwatch.com/en-gb/career/";
            string naAppend = $"pc/us/{RealID}";
            string euAppend = $"pc/eu/{RealID}";
            Boolean EU = false;
            Boolean NA = false;
            HttpClient _client = new HttpClient();
            Console.WriteLine("Created HTTP Client");
            _client.BaseAddress = new Uri(baseUrl);
            Console.WriteLine("Checking NA response");
            var responseNA = await _client.GetAsync(naAppend);

            if (responseNA.IsSuccessStatusCode)
            {
                NA = true;
            }
            var responseEU = await _client.GetAsync(euAppend);
            if (responseEU.IsSuccessStatusCode)
            {
                EU = true;
            }

            if (EU == true && NA == true)
                URL = $"http://playoverwatch.com/en-gb/career/pc/eu/{RealID}";
            if (EU == true)
                URL = $"http://playoverwatch.com/en-gb/career/pc/eu/{RealID}";
            if (NA == true)
                URL = $"http://playoverwatch.com/en-gb/career/pc/us/{RealID}";

            var config = Configuration.Default.WithDefaultLoader();
            IDocument doc = await BrowsingContext.New(config).OpenAsync(URL);
            ushort parsedCompetitiveRank = 0;
            int CompetitiveRank = 0;
            if (ushort.TryParse(doc.QuerySelector("div.competitive-rank div")?.TextContent, out parsedCompetitiveRank))
                CompetitiveRank = parsedCompetitiveRank;

            return CompetitiveRank;
        }

        private async void GreetUser(User X, Server Y)
        {
            if (Properties.Settings.Default.GreetingEnable == true)
            {
                string template = Properties.Settings.Default.GreetingText;
                template = template.Replace("[USER]", $"{X.Mention}");
                template = template.Replace("[SERVER]", "PanzerDiscord");
                template = template.Replace("[RULES]", $"{GetRules(Y).Mention}");
                await GetGeneral(Y).SendMessage(template);
            }
        }

        private async void ByeUser(User X, Server Y)
        {
            if (Properties.Settings.Default.LeaveEnable == true)
            {
                string template = Properties.Settings.Default.LeaveText;
                template = template.Replace("[USER]", $"{X.Name}");
                await GetGeneral(Y).SendMessage(template);
            }
        }

        private async void endpoll(string name, Server server, Channel channel, User user)
        {
            Dictionary<string, int> results = new Dictionary<string, int>();
            RunningPolls.TryGetValue(name, out results);
            List<string> options = new List<string>();
            List<int> amounts = new List<int>();
            string response = $"Results for poll `{name.Replace('_', ' ')}`: \n```";
            foreach (KeyValuePair<string, int> pair in results)
            {
                response = response + $"{pair.Key.Replace('_', ' ')} : {pair.Value} \n";
                options.Add(pair.Key);
                amounts.Add(pair.Value);
                Console.WriteLine($"added {pair.Key} to options and {pair.Value} to amounts");
            }
            response = response + "```";
            int winner = FindMaxIndex(amounts);
            response = response + $"\n \nThe option `{options[winner].Replace('_', ' ')}` has won with `{amounts[winner]}` votes!";
            response = response.Replace("\n", Environment.NewLine);
            await channel.SendMessage(response);
            PollMaker.Remove(name);
            RunningPolls.Remove(name);
            ServerPollRegistry.Remove(name);
            VotedUsers.Remove(name);
        }

        private int FindMaxIndex(List<int> list)
        {
            int maxVal = int.MinValue;
            foreach (int type in list)
            {
                if (type > maxVal)
                {
                    maxVal = type;
                }
            }
            return list.IndexOf(maxVal); 
        }
        //Sendaudio, don't touch. This is magic!
        private void SendAudio(string filepath, IAudioClient voiceclient)
        {
            var channelCount = audioservice.Config.Channels;
            var OutFormat = new WaveFormat(48000, 16, channelCount);
            using (var MP3Reader = new Mp3FileReader(filepath))
            using (var resampler = new MediaFoundationResampler(MP3Reader, OutFormat))
            {
                resampler.ResamplerQuality = 60;
                int blockSize = OutFormat.AverageBytesPerSecond / 50;
                byte[] buffer = new byte[blockSize];
                int byteCount;

                while((byteCount = resampler.Read(buffer, 0, blockSize)) > 0)
                {
                    if (byteCount < blockSize)
                    {
                        for (int i = byteCount; i < blockSize; i++)
                            buffer[i] = 0;
                    }
                    voiceclient.Send(buffer, 0, blockSize);

                }
            }

            
        }
        //END JOB FUNCTIONS


        //OVERWATCH
        private void RegisterOverwatch()
        {
            commands.CreateCommand("overwatch")
                .Hide()
            .Do(async (e) =>
            {
                if (e.User.HasRole(getEU(e.Server)))
                    await e.User.RemoveRoles(getEU(e.Server));
                else if (e.User.HasRole(getNA(e.Server)))
                    await e.User.RemoveRoles(getNA(e.Server));
                else if (e.User.HasRole(getAsia(e.Server)))
                    await e.User.RemoveRoles(getAsia(e.Server));
                
                await e.User.SendMessage($"Hey {e.User} and welcome to PanzerDiscord! Please tell me, do you play Overwatch? \nValid answers: \n```Yes \nNo \n```".Replace("\n", Environment.NewLine));
                UserRef.Add(e.User.Name, e.User.Id);
                Console.WriteLine($"{e.User} has called the overwatch command");
            });

        }

        //MVP
        private void RegisterMVP()
        {
            commands.CreateCommand("mvp")
                .Parameter("name", ParameterType.Required)
                .Description("Promotes <name> to MVP status")
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    var server = e.Server;

                    User u = null;
                    string findUser = e.Args[0];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if (!u.HasRole(GetMVP(e.Server)))
                    {
                        Message msg = await e.Channel.SendMessage($"Congrats {u.Mention}! You have just been promoted!");
                        await botlog.SendMessage($"{e.User} has promoted {u} to MVP status!");
                        await u.AddRoles(GetMVP(e.Server));
                    }
                    else if (u.HasRole(GetMVP(e.Server)))
                    {
                        Message msg = await e.Channel.SendMessage($"Cant promote {u} as he/she is already an MVP!");
                        await Task.Delay(7000);
                        await msg.Delete();
                    }
                });
        }

        //Mod
        private void RegisterMod()
        {
            commands.CreateCommand("mod")
                .Parameter("name")
                .Description("promotes <name> to mod status")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    var server = e.Server;

                    User u = null;
                    string findUser = e.Args[0];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if (!u.HasRole(GetMod(e.Server)))
                    {
                        Message msg = await e.Channel.SendMessage($"Congrats {u.Mention}! You have just been promoted!");
                        await botlog.SendMessage($"{GetMod(e.Server).Mention} Welcome our new moderator {u}!");
                        await u.AddRoles(GetMod(e.Server));
                    }
                    else if (u.HasRole(GetMod(e.Server)))
                    {
                        Message msg = await e.Channel.SendMessage($"Cant promote {u} as he/she is already a Mod!");
                        await Task.Delay(7000);
                        await msg.Delete();
                    }
                });
        }

        //AUTOUSER
        private void RegisterAutoUser()
        {
            commands.CreateCommand("userpromo")
                .Parameter("on/off", ParameterType.Required)
                .Description("Turns automatic user promotion <on/off>")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    string option = e.Args[0];
                    if (option == "on")
                    {
                        UserPromo = true;
                        await e.Channel.SendMessage("New users will automatically receive a green name!");
                        Properties.Settings.Default.Autouser = UserPromo;
                        Properties.Settings.Default.Save();
                    }
                    else if (option == "off")
                    {
                        UserPromo = false;
                        await e.Channel.SendMessage("New users will no longer automatically receive a green name!");
                        Properties.Settings.Default.Autouser = UserPromo;
                        Properties.Settings.Default.Save();
                    }
                    else
                    {
                        var msg1 = await e.Channel.SendMessage("This can either be on or off, nothing else.");
                        await Task.Delay(3000);
                        await msg1.Delete();
                        await e.Message.Delete();
                    }

                });
        }
        
        //PING
        private void RegisterPINGPONG()
        {
            commands.CreateCommand("PING")
                .Hide()
                .Parameter("arg1", ParameterType.Optional)
                .Do(async (e) =>
                {
                    await e.Channel.SendMessage("PONG");
                    //IEnumerable<Message> messagecache2 = await GetGamertags(e.Server).DownloadMessages(100);
                    ////messagecache = await GetGamertags(e.Server).DownloadMessages(GetGamertags(e.Server).Messages.Count());
                    //Console.WriteLine("trying foreach");
                    //List<Message> messagecache = messagecache2.ToList();
                    //foreach (Message message in messagecache)
                    //{
                    //    if (!PostedTag.Contains(message.User.Id.ToString()))
                    //        PostedTag.Add(message.User.Id.ToString());
                    //}
                    //Save(PostedTag);

                });
        }

        //UNPUNISH
        private void RegisterUnpunish()
        {
            commands.CreateCommand("unpunish")
                .Description("Unpunishes <name>")
                .Parameter("name", ParameterType.Required)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    var server = e.Server;

                    User u = null;
                    string findUser = e.Args[0];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if (!(u.HasRole(GetMod(e.Server)) && e.User.HasRole(GetMod(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                    {
                        await e.Channel.SendMessage($"{u.Mention} has been unpunished! :thumbsup:");
                        string report = $"**Action:** Unpunish\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}".Replace("\n", Environment.NewLine);
                        await botlog.SendMessage(report);
                        Boolean ismod = false;
                        WasMod.TryGetValue(u.Id, out ismod);
                        if (ismod)
                        {
                            //await u.RemoveRoles(e.Server.FindRoles("wasmod").FirstOrDefault());
                            await u.AddRoles(GetMod(e.Server));
                        }
                        unPunish(u, server);
                    }
                    else
                    {
                        var msg = await e.Channel.SendMessage("A mod cannot unpunish another mod!");
                        await Task.Delay(3000);
                        await msg.Delete();
                    }
                });
                
    }

        //PUNISH
        private void RegisterPunish()
        {
            commands.CreateCommand("punish")
                .Description("Punishes <name> for <time> minutes, unless it is too obvious, a reason should be specified at [-]")
                .Parameter("name", ParameterType.Required)
                .Parameter("time", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                var server = e.Server;

                User u = null;
                string findUser = e.Args[0];
                int time = Convert.ToInt16(e.Args[1]);
                string reason = e.Args[2];
                var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(findUser))
                {
                    if (e.Message.MentionedUsers.Count() == 1)
                        u = e.Message.MentionedUsers.FirstOrDefault();
                    else if (e.Server.FindUsers(findUser).Any())
                        u = e.Server.FindUsers(findUser).FirstOrDefault();
                    else
                        await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                }
                if ((!u.HasRole(GetMod(server)) && !u.HasRole(GetAdmin(server))) || e.User.HasRole(GetAdmin(e.Server)))
                {
                    await e.Channel.SendMessage($"{u.Mention} has been punished for {time} minutes! :oncoming_police_car: :rotating_light:");
                    string report = $"**Action:** Punish\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}\n**Time:** {time} minutes".Replace("\n", Environment.NewLine);
                    await botlog.SendMessage(report);
                        
                        WasMod.Add(u.Id, u.HasRole(GetMod(e.Server)));
                        await u.RemoveRoles(GetMod(e.Server));
                        //if (u.HasRole(GetMod(e.Server)))
                        //{
                        //    Console.WriteLine("TEST1");
                        //    await Task.Delay(500);
                        //    await u.AddRoles(e.Server.FindRoles("wasmod").FirstOrDefault());
                        //    Console.WriteLine("TEST2");
                        //    await u.RemoveRoles(GetMod(e.Server));
                        //}
                        //await Task.Delay(2000);
                    Punish(u, server);
                        //await Task.Delay(1000);
                        //if (!u.HasRole(GetPunished(e.Server)))
                        //    Punish(u, server);
                    await Task.Delay(time * 60000);
                        //if (u.HasRole(e.Server.FindRoles("wasmod").FirstOrDefault()))
                        Boolean ismod = false;
                        WasMod.TryGetValue(u.Id, out ismod);
                        if (ismod)
                        {
                            //await u.RemoveRoles(e.Server.FindRoles("wasmod").FirstOrDefault());
                            await u.AddRoles(GetMod(e.Server));
                        }
                        //await Task.Delay(1000);
                    unPunish(u, server);
                    await botlog.SendMessage($"User {u} unpunished because time expired!");
                        WasMod.Remove(u.Id);

                    }
                    else
                    {
                        var msg = await e.Channel.SendMessage($"Can't punish {u} as he/she is a moderator/admin!");
                        await botlog.SendMessage($"Mod/Admin {e.User} has tried to punish {u}. The action was denied.");
                        await Task.Delay(3000);
                        await msg.Delete();
                        await e.Message.Delete();
                    }
                });
        }

        //CHATUNMUTE
        private void RegisterChatunMute()
        {
            commands.CreateCommand("chatunmute")
                .Description("Unmutes <name> on all text chats")
                .Parameter("name", ParameterType.Required)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    User u = null;
                    string findUser = e.Args[0];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if (!(u.HasRole(GetMod(e.Server)) && e.User.HasRole(GetMod(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                    {
                        await e.Channel.SendMessage($"{u.Mention} has been chatunmuted on this server! :monkey_face:");
                        string report = $"**Action:** Chat-Unmute\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}".Replace("\n", Environment.NewLine);
                        await botlog.SendMessage(report);
                        //if (u.HasRole(e.Server.FindRoles("wasmod").FirstOrDefault()))
                        //{
                        //    await u.RemoveRoles(e.Server.FindRoles("wasmod").FirstOrDefault());
                        //    await u.AddRoles(GetMod(e.Server));
                        //}
                        Boolean ismod = false;
                        WasMod.TryGetValue(u.Id, out ismod);
                        if (ismod)
                        {
                            await u.AddRoles(GetMod(e.Server));
                            await Task.Delay(1000);
                            Console.WriteLine("test1");
                        }
                        WasMod.Remove(u.Id);
                        await u.RemoveRoles(GetMuted(e.Server));
                    }
                    else
                    {
                        var msg = await e.Channel.SendMessage("A mod cannot unmute another mod!");
                        await Task.Delay(3000);
                        await msg.Delete();
                    }
                });
        }

        //CHATMUTE
        private void RegisterChatMute()
        {
            commands.CreateCommand("chatmute")
                .Description("Mutes <name> on all textchats, unless its too obvious a reason should be specified at [-]")
                .Parameter("name", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    User u = null;
                    string findUser = e.Args[0];
                    string reason = e.Args[1];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if ((!u.HasRole(GetMod(e.Server)) && !u.HasRole(GetAdmin(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                    {
                        await e.Channel.SendMessage($"{u.Mention} has been chatmuted on this server! :speak_no_evil:");
                        string report = $"**Action:** Chat-Mute\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}".Replace("\n", Environment.NewLine);
                        await botlog.SendMessage(report);
                        //if (u.HasRole(GetMod(e.Server)))
                        //{
                        //    await u.AddRoles(e.Server.FindRoles("wasmod").FirstOrDefault());
                        //    await Task.Delay(2000);
                        WasMod.Add(u.Id, u.HasRole(GetMod(e.Server)));
                        await u.RemoveRoles(GetMod(e.Server));
                        //}
                        await u.AddRoles(GetMuted(e.Server));

                    }
                    else
                    {
                        var msg = await e.Channel.SendMessage($"Can't chatmute {u} as he/she is a moderator/admin!");
                        await botlog.SendMessage($"Mod/Admin {e.User} has tried to chatmute {u}. The action was denied.");
                        await Task.Delay(3000);
                        await msg.Delete();
                        await e.Message.Delete();
                    }

                });
        }

        //VOICEUNMUTE
        private void RegisterUnvoicemute()
        {
            commands.CreateCommand("voiceunmute")
                .Description("Unmutes <name> un all voice channels")
                .Parameter("name", ParameterType.Required)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    Boolean allowed = e.User.ServerPermissions.ManageChannels;

                    

                    User u = null;
                    string findUser = e.Args[0];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();


                    if (allowed == true)
                    {
                        if (!string.IsNullOrWhiteSpace(findUser))
                        {
                            if (e.Message.MentionedUsers.Count() == 1)
                                u = e.Message.MentionedUsers.FirstOrDefault();
                            else if (e.Server.FindUsers(findUser).Any())
                                u = e.Server.FindUsers(findUser).FirstOrDefault();
                            else
                                await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                        }

                        if (!(u.HasRole(GetMod(e.Server)) && e.User.HasRole(GetMod(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                        {
                            await e.Channel.SendMessage($"{u.Mention} has been voiceunmuted from this server! :monkey_face:");
                            string report = $"**Action:** Voice-Unmute\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}".Replace("\n", Environment.NewLine);
                            await botlog.SendMessage(report);
                            Boolean ismod = false;
                            WasMod.TryGetValue(u.Id, out ismod);
                            if (ismod)
                            {
                                await u.AddRoles(GetMod(e.Server));
                                await Task.Delay(1000);
                                Console.WriteLine("test1");
                            }
                            WasMod.Remove(u.Id);
                            await u.Edit(isMuted: false);
                        }
                        else
                        {
                            var msg = await e.Channel.SendMessage("A mod cannot unmute another mod!");
                            await Task.Delay(3000);
                            await msg.Delete();
                        }

                      
                    }

                });
        }

        //VOICEMUTE
        private void RegisterVoiceMute()
        {
            commands.CreateCommand("voicemute")
                .Description("Mutes <name> on all voicechannels, unless its too obvious a reason should be specified at [-]")
                .Parameter("name", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    Boolean allowed = e.User.ServerPermissions.ManageChannels;



                    User u = null;
                    string findUser = e.Args[0];
                    string reason = e.Args[1];
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();


                    if (allowed == true)
                    {
                        if (!string.IsNullOrWhiteSpace(findUser))
                        {
                            if (e.Message.MentionedUsers.Count() == 1)
                                u = e.Message.MentionedUsers.FirstOrDefault();
                            else if (e.Server.FindUsers(findUser).Any())
                                u = e.Server.FindUsers(findUser).FirstOrDefault();
                            else
                                await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                        }
                        if ((!u.HasRole(GetMod(e.Server)) && !u.HasRole(GetAdmin(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                        {
                            await e.Channel.SendMessage($"{u.Mention} has been voicemuted from this server! :speak_no_evil:");
                            string report = $"**Action:** Voice-Mute\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}".Replace("\n", Environment.NewLine);
                            await botlog.SendMessage(report);
                            WasMod.Add(u.Id, u.HasRole(GetMod(e.Server)));
                            await u.RemoveRoles(GetMod(e.Server));
                            await u.Edit(isMuted: true);
                        }
                        else
                        {
                            var msg = await e.Channel.SendMessage($"Can't voicemute {u} as he/she is a moderator/admin!");
                            await botlog.SendMessage($"Mod/Admin {e.User} has tried to voicemute {u}. The action was denied.");
                            await Task.Delay(3000);
                            await msg.Delete();
                            await e.Message.Delete();
                        }
                    }

                });
        }

        //PERMABAN
        private void RegisterPermaBan()
        {
            commands.CreateCommand("permaban")
                .Description("Bans <name> from the server permanently")
                .Parameter("name", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {


                    User u = null;
                    string findUser = e.Args[0];
                    string reason = e.Args[1];

                    Console.WriteLine(findUser);
                    Console.WriteLine(reason);


                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    if (reason == "")
                        reason = "[No reason specified]";
                    await u.SendMessage($"Admin {e.User} has permabanned you from the PanzerDiscord Server for {reason}. If you wan't to appeal this ban, contact the admin who banned you!");
                    await Task.Delay(200);
                    await e.Server.Ban(u, 1);
                    await e.Channel.SendMessage($"{u} has been __**PERMABANNED**__ from this server!!! :hammer_pick: :hammer: :hammer: :hammer: {Environment.NewLine} http://i.imgur.com/01Ztj.gif");
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();
                    string report = $"**Action:** Permanent ban\n**Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}".Replace("\n", Environment.NewLine);
                    await botlog.SendMessage(report);

                });
        }

        //BAN
        private void RegisterBan()
        {
            commands.CreateCommand("ban")
                .Description("Bans <name> from the server for <time> hours, unless its too obvious a reason should be specified at [-]")
                .Parameter("name", ParameterType.Required)
                .Parameter("time", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
                {
                    

                    User u = null;
                    string findUser = e.Args[0];
                    string reason = e.Args[2];
                    Int32 time = Convert.ToInt32(e.Args[1]);
                    var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();


                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                if (!u.HasRole(GetMod(e.Server)) && !u.HasRole(GetAdmin(e.Server)))
                {
                        if ((time <= 24) || u.HasRole(GetAdmin(e.Server)))
                        {
                            await u.SendMessage($"Mod/Admin {Convert.ToString(e.User)} has banned you from the PanzerDiscord Server for {reason}. You can rejoin in {Convert.ToString(time)} hours if you want.");
                            await Task.Delay(200);
                            await e.Server.Ban(u);
                            await e.Channel.SendMessage($"{u} has been **banned** from this server for {time} hours! :hammer: {Environment.NewLine} http://i.imgur.com/O3DHIA5.gif");
                            string report = $"**Action:** Ban\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}\n**Time:** {time} hours".Replace("\n", Environment.NewLine);
                            await botlog.SendMessage(report);
                            await Task.Delay(time * 3600000);
                            await e.Server.Unban(u);
                            await botlog.SendMessage($"User {u} unbanned because time expired!");
                        }
                        else
                        {
                            var msg = await e.Channel.SendMessage($"Can't ban {u} over 24 hours!");
                            await botlog.SendMessage($"Mod/Admin {e.User} has tried to ban {u} for over 24 hours. The action was denied.");
                            await Task.Delay(3000);
                            await msg.Delete();
                            await e.Message.Delete();
                        }
                    }
                    else
                    {
                        var msg = await e.Channel.SendMessage($"Can't ban {u} as he/she is a moderator/admin!");
                        await botlog.SendMessage($"Mod/Admin {e.User} has tried to ban {u}. The action was denied.");
                        await Task.Delay(3000);
                        await msg.Delete();
                        await e.Message.Delete();
                    }


                });
        }

        //KICK
        private void RegisterKick()
        {
            commands.CreateCommand("kick")
                .Description("Kicks <name> from the server, unless its too obvious a reason should be specified at [-]")
                .Parameter("name", ParameterType.Required)
                .Parameter("reason", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Do(async (e) =>
               {
                   Boolean allowed = e.User.ServerPermissions.ManageChannels;



                   User u = null;
                   string findUser = e.Args[0];
                   string reason = e.Args[1];
                   var botlog = e.Server.FindChannels("#botlog").FirstOrDefault();

                   Console.WriteLine(findUser);
                   Console.WriteLine(reason);

                   if (allowed == true)
                   {
                       if (!string.IsNullOrWhiteSpace(findUser))
                       {
                           if (e.Message.MentionedUsers.Count() == 1)
                           
                               u = e.Message.MentionedUsers.FirstOrDefault();
                           
                           else if (e.Server.FindUsers(findUser).Any())
                               u = e.Server.FindUsers(findUser).FirstOrDefault();
                           else
                               await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                       }
                        if ((!u.HasRole(GetMod(e.Server)) && !u.HasRole(GetAdmin(e.Server))) || e.User.HasRole(GetAdmin(e.Server)))
                        {
                       await u.SendMessage($"Mod/Admin {Convert.ToString(e.User)} has kicked you from the PanzerDiscord Server for {reason}. You can rejoin immediately if you want.");
                       await Task.Delay(200);
                       await u.Kick();
                       await e.Channel.SendMessage($"{u} has been kicked from this server! :boot:");
                       string report = $"**Action:** Kick\n**Mod/Admin:** {Convert.ToString(e.User)}\n**Target:** {u}\n**Reason:** {reason}".Replace("\n", Environment.NewLine);
                       await botlog.SendMessage(report);
                       }
                       else
                       {
                           var msg = await e.Channel.SendMessage($"Can't kick {u} as he/she is a moderator/admin!");
                           await botlog.SendMessage($"Mod/Admin {e.User} has tried to kick {u}. The action was denied.");
                           await Task.Delay(3000);
                           await msg.Delete();
                           await e.Message.Delete();
                       }

                   }
                   
                   
               });

            
        }

        ////HELP
        //private void RegisterHelp()
        //{
        //    commands.CreateCommand("help")
                
        //        .Do(async (e) =>
        //        {
        //            Console.WriteLine($"{e.User} called help");
        //            string help2 = null;
        //            string help = "```ModBot Mark I list of commands available to users:\n \nCommand: +overwatch \nUsage: +overwatch \nDescription: This command starts the bot dialogue which will determine in which Overwatch region you play \n \nCommand: +getid \nUsage: +getid @User \nDescription: Returns the Battle.net ID of @User \n \nCommand: +getid \nUsage +getid @User \nDescription: Returns the Battle.net ID, the region and the competitive rank of @User```";
        //            if (e.User.HasRole(GetMod(e.Server)) || e.User.HasRole(GetAdmin(e.Server)))
        //                help = $"{help} \n \n \n```ModBot Mark I list of commands available to moderators:\n \nCommand: +voicemute \nUsage: +voicemute @User Reason \nDescription: Server mutes @User in all voice channels \n \nCommand: +voiceunmute \nUsage: +voiceunmute @User \nDescription: Removes server mute of @User in all voice channels \n \nCommand: +chatmute \nUsage: +chatmute @User reason \nDescription: Mutes @User in all text chat channels \n \nCommand: +chatunmute \nUsage: +chatunmute @User \nDescription: Removes mute of @User from all text chat channels \n \nCommand: +punish \nUsage: +punish @User minutes reason \nDescription: Assigns punished role to @User \n \nCommand: +unpunish \nUsage: +unpunish @User \nDescription: Removes punished role from @User \n \nCommand: +kick \nUsage: +kick @User reason \nDescription: Kicks @User from the server. \n \nCommand: +ban \nUsage: +ban @User hours reason \nDescription: Bans @User from the server for the specified amount of hours \nNote: Keep in mind that only Admins may apply bans of over 24 hours! \n \nCommand: +mvp \nUsage: +mvp @User \nDescription: Promotes @User to MVP status \n \nCommand: +delid \nUsage: +delid @User \nDescription: Deletes @User's Battle.net ID entry in the database \nIMPORTANT NOTE: ONLY USE THIS IF YOU KNOW WHAT YOU ARE DOING!!! THIS CAN BREAK THE BOT!!!```";
        //            help = help.Replace("\n", Environment.NewLine);
        //            await Task.Delay(1000);
        //            await e.User.SendMessage(help);
        //            if (e.User.HasRole(GetAdmin(e.Server)))
        //            {
        //                help2 = $"```ModBot Mark I list of commands only available to admins: \n \nCommand: +permaban \nUsage: +permaban @User \nDescription: Bans @User from the server permanently \n \nCommand: +userpromo \nUsage: +userpromo on/off \nDescription: Turns automatic user promotion of/off \n \nCommand: +mod \nUsage: +mod @User \nDescription: Promotes @User to Moderator status \n \nCommand: +greetmod \nUsage: +greetmod on/off \nDescription: Turns admin and mod notifications for newly joined users on/off \n \nCommand: +greetuser \nUsage: +greetuser on/off \nDescription: Turns greeting messages for new users on off \n \nCommand: +greetmessage \nUsage: +greetmessage [MESSAGE] \nDescription: Changes the greet message for new users to [MESSAGE], in the message [USER] becomes a mention for the new user, [SERVER] becomes the server name and [RULES] becomes a channel-mention to the #rules \n \nCommand: +leaveuser \nUsage: +leaveuser on/off \nDescription: Turns leave messages for users who leave on/off \n \nCommand: +leavemessage \nUsage: +leavemessage [MESSAGE] \nDescription: Changes the leave-message to [MESSAGE] where [USER] becomes the username of the person who left```".Replace("\n", Environment.NewLine);
        //                Console.WriteLine("sending admin help");
        //                await e.User.SendMessage(help2);
        //            }
        //            //var channel = await e.User.CreatePMChannel();
        //            //Console.WriteLine("created private channel");
        //            //var msg = await channel.SendMessage("TEST");
        //            //await Task.Delay(500);
        //            ////await commands.ShowGeneralHelp(e.User, );
        //            //await e.User.SendMessage(commands.ShowGeneralHelp(e.User, channel).ToString());
        //            //Console.WriteLine("sent help");

        //        });
        //}

        //GETID
        private void RegisterGetID()
        {
            commands.CreateCommand("getid")
                .Description("Gets the Battle.net ID of <name> if they have posted it in #gamertags, also returns server regions and competitive rank of the EU server")
                .Parameter("name")
                .Do(async (e) =>
                {
                    Console.WriteLine("Running command");
                    User u = null;
                    string ID = null;
                    string findUser = e.Args[0];
                    string region = null;
                    int rank = 0;
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                    }
                    Console.WriteLine(u.Name);
                    if (!BattlenetTags.ContainsKey(u.Id.ToString()))
                    { 
                        await e.Channel.SendMessage($"Couldn't find {u.Name}'s Battle.net tag in {GetGamertags(e.Server).Mention}! :disappointed_relieved:");
                        return;
                    }
                    BattlenetTags.TryGetValue(u.Id.ToString(), out ID);
                    var response = await e.Channel.SendMessage($"Found {u.Name}s battle.net tag! \n```Battle.net ID: {ID} \nRegion: ... \nCompetitive rank: ...```".Replace("\n", Environment.NewLine));
                    region = await GetRegion(ID);
                    await response.Edit($"Found {u.Name}s battle.net tag! \n```Battle.net ID: {ID} \nRegion: {region} \nCompetitive rank: ...```".Replace("\n", Environment.NewLine));
                    rank = await GetRank(ID);
                    await response.Edit($"Found {u.Name}s battle.net tag! \n```Battle.net ID: {ID} \nRegion: {region} \nCompetitive rank: {rank}```".Replace("\n", Environment.NewLine));

                });
        }

        //GREETMOD
        public void RegisterGreetMod()
        {
            commands.CreateCommand("greetmod")
                .Description("Turns the greeting message for Moderators and Admin in #moderation <on/off>")
                .Parameter("on/off")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    Boolean state = false;
                    string input = e.Args[0];
                    if (input == "on")
                        state = true;
                    else if (input == "off")
                        state = false;
                    else
                        await e.Channel.SendMessage("This can either be `on` or `off`");
                    await e.Channel.SendMessage($"The option: 'Announce new users to moderators' has been set to: `{input}'");
                    Properties.Settings.Default.GreetMod = state;
                });
    }

        //GREETUSERENABLE
        private void RegisterGreetUserEnable()
        {
            commands.CreateCommand("greetuser")
                .Description("Turns the welcome messages for new users <on/off>")
                .Parameter("on/off")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    Boolean state = false;
                    string input = e.Args[0];
                    if (input == "on")
                    {
                        state = true;
                        await e.Channel.SendMessage($"The option: 'Greet new users' has been set to: `{input}`");
                        Properties.Settings.Default.GreetingEnable = state;
                    }
                    else if (input == "off")
                    {
                        state = false;
                        await e.Channel.SendMessage($"The option: 'Greet new users' has been set to: `{input}`");
                        Properties.Settings.Default.GreetingEnable = state;
                    }
                    else
                        await e.Channel.SendMessage("This can either be `on` or `off`");
                });
        }

        //GREETINGMESSAGE
        private void RegisterGreetMessage()
        {
            commands.CreateCommand("greetmessage")
                .Description("Sets the welcome message for new users to [-] where [USER] is a user mention, [SERVER] is the name of the server and [RULES] is a mention of the #rules channel")
                .Parameter("message", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    string input = e.Args[0];
                    Properties.Settings.Default.GreetingText = input;
                    await e.Channel.SendMessage("Greeting message has been changed!");
                });
        }

        //LEAVEENABLE
        private void RegisterLeaveEnable()
        {
            commands.CreateCommand("leaveuser")
                .Description("Turns the farewell message for users who just left <on/off>")
                .Parameter("on/off")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    Boolean state = false;
                    string input = e.Args[0];
                    if (input == "on")
                    {
                        state = true;
                        await e.Channel.SendMessage($"The option: 'Say bye to leaving users' has been set to: `{input}`");
                        Properties.Settings.Default.LeaveEnable = state;
                    }
                    else if (input == "off")
                    {
                        state = false;
                        await e.Channel.SendMessage($"The option: 'Say bye to leaving users' has been set to: `{input}`");
                        Properties.Settings.Default.LeaveEnable = state;
                    }
                    else
                        await e.Channel.SendMessage("This can either be `on` or `off`");
                });

        }

        //LEAVEMESSAGE
        private void RegisterLeaveMessage()
        {
            commands.CreateCommand("leavemessage")
                .Description("Sets the farewell message to [-], where [USER] is he user name")
                .Parameter("message", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    string input = e.Args[0];
                    Properties.Settings.Default.LeaveText = input;
                    await e.Channel.SendMessage("Leave message has been changed!");
                });
        }

        //MAKEPOLL
        private void RegisterMakePoll()
        {
            commands.CreateCommand("makepoll")
                .Description("Creates a public poll with <name> which will run for <time> hours, substitute [-] with the options you want the poll to have, seperated by `;`. For the <name> underscores `_` will become spaces. Example: \n```+makepoll test 1 this is option 1;this is option 2;this is option 3```")
                .Parameter("name", ParameterType.Required)
                .Parameter("time", ParameterType.Required)
                .Parameter("options", ParameterType.Unparsed)
                .MinPermissions((int)PermissionLevel.Member)
                .Do(async (e) =>
                {
                    string name = e.Args[0];
                    int time = 0;
                    List<string> optionlist = e.Args[2].Split(';').ToList();
                    Boolean duplicate = false;
                    foreach (string option in optionlist)
                    {
                        Console.WriteLine(option);
                    }
                    Dictionary<string, int> options = new Dictionary<string, int>();
                    ulong tempserver2;
                    Server tempserver = null;
                    if (ServerPollRegistry.ContainsKey(name))
                    {
                        ServerPollRegistry.TryGetValue(name, out tempserver2);
                        tempserver = discord.GetServer(tempserver2);
                    }

                    if (tempserver == e.Server)
                    {
                        await e.Channel.SendMessage("A poll with that name is already running on this server!");
                        return;
                    }
                    if (!(Int32.TryParse(e.Args[1], out time)))
                    {
                        await e.Channel.SendMessage("Syntax error! The correct syntax for this command is `+makepoll [POLL NAME] [TIME IN HOURS] [POLL OPTIONS]`");
                        return;
                    }
                    foreach (string option in optionlist)
                    {
                        if (!options.ContainsKey(option))
                        {
                            options.Add(option, 0);
                            Console.WriteLine($"added {option}, 0 to options");
                        }
                        else
                            duplicate = true;
                    }
                    if (duplicate == true)
                    {
                        await e.Channel.SendMessage($"Hey {e.User.Mention}! You can't have two identical options in one poll!");
                        return;
                    }
                    ServerPollRegistry.Add(name, e.Server.Id);
                    RunningPolls.Add(name, options);
                    string temp = e.Args[2].Replace(";", "` and `");
                    string optionmsg = null;
                    foreach (string option in optionlist)
                    {
                        optionmsg = $"{optionmsg}+vote {name} {option} \n";
                    }
                    await e.Channel.SendMessage($"The poll `{name.Replace('_', ' ')}` is now running for {time} hour(s)! The options are `{temp}`. Use the following command to view the poll: `+viewpoll {name}` Use the following command to end the poll: `+endpoll {name}` \nThe commands to vote for this poll are:```{optionmsg}```");
                    PollMaker.Add(name, e.User.Id);
                    List<ulong> voted = new List<ulong>();
                    voted.Add(e.User.Id);
                    VotedUsers.Add(name, voted);
                    await Task.Delay(time * 3600000);
                    if (RunningPolls.ContainsKey(name))
                        endpoll(name, e.Server, e.Channel, e.User);
                });
    }

        //VOTEPOLL
        private void RegisterVote()
        {
            commands.CreateCommand("vote")
                .Hide()
                .Parameter("name", ParameterType.Required)
                .Parameter("option", ParameterType.Unparsed | ParameterType.Required)
                .Do(async (e) =>
                {
                    string name = e.Args[0];
                    string option = e.Args[1];
                    if (!RunningPolls.ContainsKey(name))
                    {
                        await e.Channel.SendMessage("No such poll exists on this server!");
                        return;
                    }
                    ulong temp;
                    ServerPollRegistry.TryGetValue(name, out temp);
                    Server server = discord.GetServer(temp);
                    if (e.Server != server)
                    {
                        await e.Channel.SendMessage("No such poll exists on this server!");
                        return;
                    }
                    Console.WriteLine("poll exists");
                    Dictionary<string, int> results = new Dictionary<string, int>();
                    RunningPolls.TryGetValue(name, out results);
                    if (!results.ContainsKey(option))
                    {
                        await e.Channel.SendMessage($"{option} is not a valid option for the {name} poll!");
                        return;
                    }
                    ulong temp1;
                    PollMaker.TryGetValue(name, out temp1);
                    User maker = e.Server.GetUser(temp1);
                    if ((maker == e.User) && (!e.User.HasRole(GetAdmin(e.Server))))
                    {
                        await e.Channel.SendMessage($"Hey {e.User.Mention}! You have created this poll, you can't vote for it!");
                        return;
                    }
                    List<ulong> voted = new List<ulong>();
                    VotedUsers.TryGetValue(name, out voted);
                    if ((voted.Contains(e.User.Id)) && (!e.User.HasRole(GetAdmin(e.Server))))
                    {
                        await e.Channel.SendMessage($"Hey {e.User.Mention}! It seems like you have voted already on this poll, you can only vote once.");
                        return;
                    }
                    Console.WriteLine("user hasnt voted yet");
                    int amount = 0;
                    results.TryGetValue(option, out amount);
                    results.Remove(option);
                    amount = amount + 1;
                    Console.WriteLine("added 1 to amount");
                    results.Add(option, amount);
                    RunningPolls.Remove(name);
                    RunningPolls.Add(name, results);
                    await e.Channel.SendMessage($"Hey {e.User.Mention}! Your vote has been recorded successfully!");
                    voted.Add(e.User.Id);
                    VotedUsers.Remove(name);
                    VotedUsers.Add(name, voted);
                });
        }

        //VIEWPOLL
        private void RegisterViewPoll()
        {
            commands.CreateCommand("viewpoll")
                .Description("Views the current state of an ongoing poll called <name>")
                .Parameter("name", ParameterType.Required)
                .Do(async (e) =>
                {
                    string name = e.Args[0];
                    if (!RunningPolls.ContainsKey(name))
                    {
                        await e.Channel.SendMessage("No such poll exists on this server!");
                        return;
                    }
                    ulong temp;
                    ServerPollRegistry.TryGetValue(name, out temp);
                    Server server = discord.GetServer(temp);
                    if (e.Server != server)
                    {
                        await e.Channel.SendMessage("No such poll exists on this server!");
                        return;
                    }
                    Dictionary<string, int> results = new Dictionary<string, int>();
                    RunningPolls.TryGetValue(name, out results);
                    List<string> options = new List<string>();
                    List<int> amounts = new List<int>();
                    string response = $"Results for poll `{name.Replace('_', ' ')}`: \n```";
                    foreach (KeyValuePair<string, int> pair in results)
                    {
                        response = response + $"{pair.Key} : {pair.Value} \n";
                        options.Add(pair.Key);
                        amounts.Add(pair.Value);
                        Console.WriteLine($"added {pair.Key} to options and {pair.Value} to amounts");
                    }
                    response = response + "```";
                    int winner = FindMaxIndex(amounts);
                    response = response + $"\n \n`{options[winner]}` is currently winning with `{amounts[winner]}` votes!";
                    response = response.Replace("\n", Environment.NewLine);
                    await e.Channel.SendMessage(response);

                });
        }

        //CLOSEPOLL
        private void RegisterClosePoll()
        {
            commands.CreateCommand("endpoll")
                .Description("Ends a poll with <name> before it has expired, can only be used by the poll creator and staff")
                .Parameter("name", ParameterType.Required)
                .Do(async (e) =>
                {
                    string name = e.Args[0];
                    ulong temp;
                    PollMaker.TryGetValue(name, out temp);
                    User maker = e.Server.GetUser(temp);
                    if ((e.User == maker) || (e.User.HasRole(GetMod(e.Server))) || (e.User.HasRole(GetAdmin(e.Server))))
                    {
                        if (!RunningPolls.ContainsKey(name))
                        {
                            await e.Channel.SendMessage("No such poll exists on this server!");
                            return;
                        }
                        ulong temp1;
                        ServerPollRegistry.TryGetValue(name, out temp1);
                        Server server = discord.GetServer(temp1);
                        if (e.Server != server)
                        {
                            await e.Channel.SendMessage("No such poll exists on this server!");
                            return;
                        }
                        endpoll(name, e.Server, e.Channel, e.User);
                    }
                });
        }

        //STOP
        private void RegisterStop()
        {
            commands.CreateCommand("stop")
                .Description("Stops what ever sound the ModBot is currently playing and makes it leave")
                .MinPermissions((int)PermissionLevel.ServerOwner)
                .Do(async (e) =>
                {
                    playing = false;
                    var audioclient = audioservice.GetClient(e.Server);
                    audioclient.Clear();
                    await audioclient.Disconnect();
                });
        }

        //PLAY
        private void RegisterPlay()
        {
            commands.CreateCommand("play")
                .Description("Connects ModBot to your current voicechannel and plays <sound>, disconnects when <sound> is over. You can submit sounds to Sqeed, however they must be a maximum of 10 seconds long and be in .mp3 format")
                .MinPermissions((int)PermissionLevel.Member)
                .Parameter("sound", ParameterType.Required)
                .Do(async (e) =>
                {
                    if (playing == true)
                    {
                        await e.Channel.SendMessage("Currently playing a sound!");
                        return;
                    }
                    if (UsedSoundBoard.Contains(e.User.Id))
                    {
                        await e.Channel.SendMessage($"Hey {e.User.Mention}! You can only use this every 1 minute!");
                        return;
                    }
                    if (!(e.User.HasRole(GetMod(e.Server))) && !(e.User.HasRole(GetAdmin(e.Server))))
                        UsedSoundBoard.Add(e.User.Id);
                    DirectoryInfo d = new DirectoryInfo(@"sounds\");
                    FileInfo[] Files = d.GetFiles("*.mp3");
                    string str = "";
                    foreach (FileInfo file in Files)
                    {
                        str = NoMp3.Match(file.Name).Groups[1].Value + " \n" + str;
                    }
                    if (!str.Contains(e.Args[0]))
                    {
                        string msg = $"The option you specified is not valid! Here are the valid options: \n ```{str}```".Replace("\n", Environment.NewLine);
                        await e.Channel.SendMessage(msg);
                        return;
                    }
                    Console.WriteLine(str);
                    var name = @"sounds\" + $"{e.Args[0]}.mp3";
                    var channel = e.User.VoiceChannel;
                    if (channel.Name.Contains("Private"))
                    {
                        await e.Channel.SendMessage($"Hey {e.User.Mention}! This command is not allowed in the private voicechannel!");
                        return;
                    }
                    Console.WriteLine("atempting join");
                    var voiceclient = await audioservice.Join(channel);
                    playing = true;
                    Console.WriteLine("playing sound");
                    SendAudio(name, voiceclient);
                    await Task.Delay(3000);
                    await voiceclient.Disconnect();
                    playing = false;
                    await Task.Delay(60000);
                    UsedSoundBoard.Remove(e.User.Id);
                    Console.WriteLine("playing sound");
                    await voiceclient.Disconnect();
                });
    }

        //CLEAR
        private void RegisterClear()
        {
            commands.CreateCommand("clear")
                .Description("Clears <amount> messages in the channel it is used in, will only delete <name>'s messages in the <amount> messages if specified")
                .MinPermissions((int)PermissionLevel.ChannelModerator)
                .Parameter("amount", ParameterType.Required)
                .Parameter("name", ParameterType.Optional)
                .Do(async (e) =>
                {
                    User u = null;
                    string findUser = e.Args[1];
                    int amount;
                    Message msg = null;
                    List<Message> Targets = new List<Message>();
                    if ((!Int32.TryParse(e.Args[0], out amount)) || (e.Args[0] == "0"))
                    {
                        await e.Channel.SendMessage("Syntax error! The correct usage for this command is: `+clear [AMOUNT] [NAME]` where `[NAME]` is optional");
                        return;
                    }
                    if (amount > 100)
                    {
                        await e.Channel.SendMessage("The current maximum for the amount of messages to be deleted is 100!");
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(findUser))
                    {
                        if (e.Message.MentionedUsers.Count() == 1)
                            u = e.Message.MentionedUsers.FirstOrDefault();
                        else if (e.Server.FindUsers(findUser).Any())
                            u = e.Server.FindUsers(findUser).FirstOrDefault();
                        else
                        {
                            await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                            return;
                        }
                    }
                    var messages = await e.Channel.DownloadMessages(amount + 1);
                    if (u != null)
                    {
                        List<Message> MessageTemp = messages.ToList();
                        foreach (Message message in MessageTemp)
                        {
                            if (message.User == u)
                                Targets.Add(message);
                        }
                        messages = Targets.ToArray();
                    }
                    if (!e.Channel.IsPrivate)
                    {
                        await e.Channel.DeleteMessages(messages);
                        if (u == null)
                        {
                            msg = await e.Channel.SendMessage($"Cleared {amount} messages! :ok_hand:");
                        }
                        if (u != null)
                        {
                            msg = await e.Channel.SendMessage($"Cleared {Targets.Count} messages belonging to {u.Name} from the most recent {amount} messages! :ok_hand:");
                        }
                        await Task.Delay(7000);
                        await msg.Delete();
                        await e.Message.Delete();
                    }
                });
    }

        //APPROVEINVITE
        private void RegisterApproveInvite()
        {
            commands.CreateCommand("private")
                 .Description("Sends an invite to the private voice channel to <name>")
                 .MinPermissions((int)PermissionLevel.ServerOwner)
                 .Parameter("name", ParameterType.Required)
                 .Do(async (e) =>
                 {
                     Channel channel = e.Server.GetChannel(233698229005451264);
                     User u = null;
                     string findUser = e.Args[0];
                     if (!string.IsNullOrWhiteSpace(findUser))
                     {
                         if (e.Message.MentionedUsers.Count() == 1)
                             u = e.Message.MentionedUsers.FirstOrDefault();
                         else if (e.Server.FindUsers(findUser).Any())
                             u = e.Server.FindUsers(findUser).FirstOrDefault();
                         else
                         {
                             await e.Channel.SendMessage($"I was unable to find a user like `{findUser}`");
                             return;
                         }
                     }
                     if (u.VoiceChannel == null)
                     {
                         Console.WriteLine("null");
                         Private.Add(u.Id);
                         await u.SendMessage("Hey, Sqeed has invited you to the private channel! Join any voicechannel and I will move you!");
                     }
                     else if (u.VoiceChannel != null)
                     {
                         await u.Edit(null, null, e.Server.GetChannel(233698229005451264));
                         Console.WriteLine("notnull");
                     }
                     Console.WriteLine(u.VoiceChannel);
                     if (RequestedInv.Contains(u.Id))
                        RequestedInv.Remove(u.Id);
                     int i = 300000;
                     //System.Timers.Timer timer = new System.Timers.Timer(i);
                     //timer.Enabled = true;
                     //timer.Elapsed += (s, r) =>
                     //{
                     //    Private.Remove(u.Id);
                     //};
                     //timer.Start();
                     Console.WriteLine("timer started");
                     //while (Private.Contains(u.Id))
                     //{
                     //    Console.WriteLine("LOOP");
                     //    if (u.VoiceChannel != null)
                     //    {
                     //        Console.WriteLine("HIT");
                     //        await u.Edit(null, null, e.Server.GetChannel(233698229005451264));
                     //        Private.Remove(u.Id);
                     //        break;
                     //    }
                     //    else if (u.VoiceChannel == null)
                     //        Console.WriteLine("NOPE");
                     //}
                     //Private.Remove(u.Id);
                     //Console.WriteLine("removed private");
                     await Task.Delay(i);
                     if (Private.Contains(u.Id))
                     Private.Remove(u.Id);
                 });
     }

        public void Log(object sender, LogMessageEventArgs e)
        {
            Console.WriteLine(e.Message);
        }
        private int PermissionResolver(User user, Channel channel)
        {
            if (!channel.IsPrivate)
            {
                if (user == channel.Server.Owner)
                    return (int)PermissionLevel.BotOwner;
                if (user.Server != null)
                {
                    if (user == channel.Server.Owner)
                        return (int)PermissionLevel.ServerOwner;

                    var serverPerms = user.ServerPermissions;
                    if (serverPerms.ManageRoles)
                        return (int)PermissionLevel.ServerAdmin;
                    if (serverPerms.ManageMessages && serverPerms.KickMembers && serverPerms.BanMembers)
                        return (int)PermissionLevel.ServerModerator;

                    var channelPerms = user.GetPermissions(channel);
                    if (channelPerms.ManagePermissions)
                        return (int)PermissionLevel.ChannelAdmin;
                    if (channelPerms.ManageMessages)
                        return (int)PermissionLevel.ChannelModerator;
                    if (channelPerms.AttachFiles)
                        return (int)PermissionLevel.Member;
                }
                return (int)PermissionLevel.Newb;
            }
            else
            {
                if (GetPD().GetUser(user.Id).HasRole(GetMod(GetPD())))
                    return (int)PermissionLevel.ChannelModerator;
                if (GetPD().GetUser(user.Id).HasRole(GetAdmin(GetPD())))
                    return (int)PermissionLevel.BotOwner;
                if (GetPD().GetUser(user.Id).HasRole(GetMember(GetPD())))
                    return (int)PermissionLevel.Member;
                else
                    return (int)PermissionLevel.Newb;
            }
        }
    }
}
