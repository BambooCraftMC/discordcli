using Discord;
using Discord.WebSocket;
using Microsoft.VisualBasic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;

public static class conf
{
    public static string FMUsername { get; set; } = "";
    public static string FMApiKey { get; set; } = "";

    public static string Status { get; set; } = "";
    private static readonly string FilePath = "config.json";

    public static void Save()
    {
        var data = new
        {
            FMUsername,
            FMApiKey,
            Status
        };

        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            })
        );
    }

    public static void Load()
    {
        if (!File.Exists(FilePath))
            return;

        var json = File.ReadAllText(FilePath);

        var data = JsonSerializer.Deserialize<ConfigData>(json);

        if (data == null)
            return;

        FMUsername = data.FMUsername;
        FMApiKey = data.FMApiKey;
        Status = data.Status;
    }

    private class ConfigData
    {
        public string FMUsername { get; set; } = "";
        public string FMApiKey { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
class Program
{
    static string msgtosend = "";
    static string alert = "No alerts";
    private static int pullcnt = 3;
    private static DiscordSocketClient _client;
    public static List<(string, ulong, string)> allChannels = new List<(string, ulong, string)>();
    static async Task Main(string[] args)
    {
        conf.Load();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.WriteLine("TOKEN:");
        string token = "";
        if (File.Exists("token.txt"))
        {
            token = File.ReadAllText("token.txt");
            if (token.StartsWith("PWD."))
            {
                try
                {
                    Console.WriteLine("TOKEN IS ENCRYPTED! PLEASE ENTER PASSWORD:");
                    string password = Console.ReadLine();
                    byte[] enckey = SHA256.HashData(Encoding.UTF8.GetBytes(password));
                    string base64 = token.Substring(4);
                    byte[] encryptedDataWithIV = Convert.FromBase64String(base64);
                    byte[] iv = new byte[16];
                    byte[] encryptedData = new byte[encryptedDataWithIV.Length - iv.Length];
                    Buffer.BlockCopy(encryptedDataWithIV, 0, iv, 0, iv.Length);
                    Buffer.BlockCopy(encryptedDataWithIV, iv.Length, encryptedData, 0, encryptedData.Length);
                    using Aes aes = Aes.Create();
                    aes.Key = enckey;
                    aes.IV = iv;
                    using MemoryStream ms = new(encryptedData);
                    using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                    using StreamReader sr = new(cs);
                    token = sr.ReadToEnd();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to decrypt token: " + ex.Message);
                    Console.WriteLine("Token:");
                    token = Console.ReadLine();
                }
            }
            Console.WriteLine("TOKEN LOADED FROM FILE");
        }
        else
            token = Console.ReadLine();
        Console.WriteLine("LOGGING IN...");
        // require all intents be enabled in discord.com/developers/applications
        DiscordSocketConfig intents = new DiscordSocketConfig() { GatewayIntents = Discord.GatewayIntents.All };
        //initalize the client
        _client = new DiscordSocketClient(intents);
        //subscribe to logging and message recived events
        _client.Log += Log;
        _client.MessageReceived += MessageRecived;
        _client.UserIsTyping += UserIsTyping;

        // login with the token
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        _client.Ready += () =>
        {

            _client.SetGameAsync(conf.Status);

            Console.WriteLine("READY!");
            //list all channels
            //and populate channel list for rest of runtime
            var guilds = _client.Guilds;
            foreach (var guild in guilds)
            {
                Console.WriteLine(guild.Name);
                //list all channels
                var channels = guild.Channels;
                allChannels.Add(("\nSRV>>", 0, guild.Name + "<<"));
                foreach (var channel in channels)
                {
                    if (channel.GetChannelType() != ChannelType.Category && channel.GetChannelType() != ChannelType.Stage)
                    {
                        Console.WriteLine($"-  {channel.Name}({channel.Id})");
                        allChannels.Add(("- " + channel.Name, channel.Id, channel.Guild.Name + ((channel.GetChannelType() == ChannelType.Voice) ? "-voice" : "-txt")));
                    }
                }
                var users = guild.Users;
                foreach (var user in users)
                {
                    Console.WriteLine($"- USR.{user.Username}");
                    if (!allChannels.Contains(($"USR.{user.Username}", user.Id, "DMS")))
                        allChannels.Add(($">USR.{user.Username}", user.Id, "DMS"));
                }


            }
            _ = OpenSelector();
            return Task.CompletedTask;
        };
        await Task.Delay(-1);
    }

    private static async Task UserIsTyping(Cacheable<IUser, ulong> cacheable1, Cacheable<IMessageChannel, ulong> cacheable2)
    {
        // check if is in openchannelid and if so cw(<username> typing...)
        if (cacheable1.Id == _client.CurrentUser.Id)
            return;
        if (openChannelID != 0 && cacheable2.Id == openChannelID)
        {
            var user = await cacheable1.GetOrDownloadAsync();
            alert += $"\n{user.Username} is typing...";

        }
    }

    // opens the channel selector
    private static async Task OpenSelector()
    {
        int selected = 0;
        while (true)
        {
            // draw the channels with index 0
            DrawChannels(selected).GetAwaiter().GetResult();
            //read a key for arrow key nav
            var key = Console.ReadKey();
            // simple arrow key select by index menu
            if (key.Key == ConsoleKey.UpArrow)
            {
                selected--;
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                selected++;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // get the channel id from the selected channel
                var channelid = allChannels[selected].Item2;
                // seperate users from normal text channels. 
                //still oddly flags catagorys as text channels but i cant fix that rn
                if (allChannels[selected].Item1.StartsWith(">USR"))
                {
                    var chan = _client.GetUserAsync(channelid).GetAwaiter().GetResult().CreateDMChannelAsync().GetAwaiter().GetResult() as IMessageChannel;
                    _ = OpenChannel(chan);
                }
                else if (allChannels[selected].Item1.StartsWith("\nSRV"))
                {
                    Console.WriteLine("INVALID CHANNEL!");
                    _ = OpenSelector();
                    return;
                }
                else
                {
                    Console.WriteLine(channelid);
                    var chan = _client.GetChannelAsync(channelid).GetAwaiter().GetResult() as IMessageChannel;
                    _ = OpenChannel(chan);
                }
                break;
            }

        }
    }
    // draw selected index for the arrow key menu in the selector
    private static async Task DrawChannels(int selected)
    {
        Console.Clear();
        foreach (var channel in allChannels)
        {
            if (allChannels.IndexOf(channel) == selected)
            {
                Console.BackgroundColor = ConsoleColor.Green;
                Console.WriteLine($"> {channel.Item1}({channel.Item3})");
                Console.BackgroundColor = ConsoleColor.Black;
            }
            else
            {
                Console.WriteLine($"  {channel.Item1}({channel.Item3})");
            }
        }
    }
    private static ulong openChannelID = 0;
    private static async Task MessageRecived(SocketMessage msg)
    {
        //if channel is open then refresh the open channel
        if (openChannelID != 0 && msg.Channel.Id == openChannelID)
        {

            var list = msgs.ToList();
            list.Add(msg);
            if (list.Count > pullcnt)
                list = list[^pullcnt..];
            msgs = list;
            QuickDrawMSG.Add($"<{msg.Timestamp.LocalDateTime.Hour}:{msg.Timestamp.LocalDateTime.Minute}>({msg.Author.Username}){msg.Author.GlobalName}<@{msg.Author.Id}> >> {msg.Content} {string.Join(", ", msg.Attachments.Select(e => e.Url))}");
            if (QuickDrawMSG.Count > pullcnt)
            {
                QuickDrawMSG = QuickDrawMSG[^pullcnt..];
            }

            _ = QuickDraw();
            List<string> thing = alert.Split("\n").ToList();
            thing.RemoveAll(x => Regex.IsMatch(x, @"^.* is typing\.\.\.$"));
            alert = string.Join("\n", thing);
        }
        else
        {
            alert = $"{msg.Author.Username} messaged you in {msg.Channel.Name} >> {msg.Content}";
            QuickDraw();
        }

    }
    static List<string> QuickDrawMSG = new List<string>();
    static string QuickDrawHeader = "";
    static List<string> QuickDrawother = new List<string>();
    private static async Task QuickDraw()
    {
        Console.Clear();
        Console.WriteLine(QuickDrawHeader);

        if (QuickDrawMSG.Count > pullcnt)
        {
            QuickDrawMSG = QuickDrawMSG[^pullcnt..];
        }
        int i = 0;
        foreach (string msg in QuickDrawMSG)
        {
            Console.WriteLine($"[{i}]{msg}");
            i++;
        }
        Console.WriteLine(String.Join("\n", QuickDrawother));
        Console.WriteLine("\n" + alert);
        Console.Write(msgtosend);
    }
    private static async Task DrawOpenChannel()
    {
        QuickDrawother.Clear();
        var chan = await _client.GetChannelAsync(openChannelID) as IMessageChannel;
        Console.Clear();
        Console.WriteLine($"   >>{chan.Name}<<");
        QuickDrawHeader = $"   >>{chan.Name}<<";
        int i = 0;
        var messages = msgs.ToList();
        if (messages.Count > pullcnt)
            messages = messages[^pullcnt..];
        msgs = messages;
        foreach (var msg in messages)
        {
            string StringToAdd =
                    $"<{msg.Timestamp.LocalDateTime.Hour}:{msg.Timestamp.LocalDateTime.Minute}>" +
                    $"({msg.Author.Username})" +
                    $"{msg.Author.GlobalName}" +
                    $"<@{msg.Author.Id}> " +
                    $">> {msg.Content} ";
            if (msg.Reference != null)
            {
                var refmsg = await chan.GetMessageAsync(msg.Reference.MessageId.Value);

                if (refmsg != null)
                {
                    StringToAdd += $"(reply to: {refmsg.Author.Username} >> {refmsg.Content}) " +
                    $"{string.Join(", ", msg.Attachments.Select(e => e.Url))} " +
                    $"{string.Join(", ", msg.Attachments.Select(e => e.Url))}";
                }
                else
                {
                    StringToAdd += $"(reply to: <DELETED>) ";
                }
            }
            StringToAdd += $"{string.Join(", ", msg.Attachments.Select(e => e.Url))}";
            Console.WriteLine($"[{i}]"+ StringToAdd);
            QuickDrawMSG.Add(StringToAdd);
            if (msg.Components.Any())
            {
                PrintComponents(msg.Components);
            }
            if (msg.Embeds.Any())
            {
                Console.WriteLine("it contains embeds");
                var embed = msg.Embeds.FirstOrDefault();

                PrintEmbed(embed);
            }
            i++;
        }
        Console.WriteLine($"\n{alert}");
        Console.Write(msgtosend);
    }
    static void PrintEmbed(IEmbed embed)
    {
        var totalwritten = "";
        Console.WriteLine();
        totalwritten += "\n";
        Console.WriteLine("╭──────────────────────────────────────────────────────────────");
        totalwritten += "╭──────────────────────────────────────────────────────────────\n";

        // Title
        if (!string.IsNullOrWhiteSpace(embed.Title))
        {
            Console.WriteLine($"│ {embed.Title}");
            totalwritten += $"│ {embed.Title}\n";

            if (!string.IsNullOrWhiteSpace(embed.Url))
            {
                Console.WriteLine($"│ {embed.Url}");
                totalwritten += $"│ {embed.Url}\n";
            }


            Console.WriteLine("│");
            totalwritten += "│\n";
        }
        //fielnds
        if (embed.Fields.Any())
        {
            foreach (var field in embed.Fields)
            {
                Console.WriteLine($"│ {field.Name}");
                totalwritten += $"│ {field.Name}\n";
                foreach (var line in field.Value.Split('\n'))
                {
                    Console.WriteLine($"│ {line.TrimEnd('\r')}");
                    QuickDrawother.Add($"│ {line.TrimEnd('\r')}");
                }
                Console.WriteLine("│");
                totalwritten += "│\n";
            }
        }
        if (!string.IsNullOrEmpty(embed?.Footer?.Text))
        {
            Console.WriteLine($"│ {embed.Footer.Value.Text}");
            totalwritten += $"│ {embed.Footer.Value.Text}\n";
        }
        // Description
        if (!string.IsNullOrWhiteSpace(embed.Description))
        {
            foreach (var line in embed.Description.Split('\n'))
            {
                Console.WriteLine($"│ {line.TrimEnd('\r')}");
                totalwritten += $"│ {line.TrimEnd('\r')}\n";
            }

            Console.WriteLine("│");
            totalwritten += "│\n";
        }

        // Fields
        foreach (var field in embed.Fields)
        {
            Console.WriteLine($"│ {field.Name}");
            totalwritten += $"│ {field.Name}\n";

            foreach (var line in field.Value.Split('\n'))
            {
                Console.WriteLine($"│ {line.TrimEnd('\r')}");
                QuickDrawother.Add($"│ {line.TrimEnd('\r')}");
            }

            Console.WriteLine("│");
            totalwritten += "│\n";
        }


        Console.WriteLine("╰──────────────────────────────────────────────────────────────");
        totalwritten += "╰──────────────────────────────────────────────────────────────\n";
        Console.WriteLine();
        totalwritten += "\n";
        var lst = QuickDrawMSG.Last();
        QuickDrawMSG.Remove(lst); // remove last
        QuickDrawMSG.Add(lst + totalwritten);

    }


    static void PrintComponents(IReadOnlyCollection<IMessageComponent> components)
    {
        Console.WriteLine();
        Console.WriteLine("╭──────────────────────────────────────────────────────────────");

        foreach (var component in components)
            PrintComponent(component);

        Console.WriteLine("╰──────────────────────────────────────────────────────────────");
    }

    public static void PrintComponent(IMessageComponent component)
    {
        Console.WriteLine(component.GetType());
        if (component is Discord.ContainerComponent eee)
        {
            foreach (var comp in eee.Components)
            {
                if (comp is SectionComponent dawdd)
                {
                    foreach (var tee in dawdd.Components)
                    {
                        if (tee is TextDisplayComponent piss)
                        {
                            Console.WriteLine(piss.Content);
                        }
                    }
                    //dawdd.Accessory
                }
            }

        }
    }

    // refresh the open channel so that its populated with new messages
    public static IEnumerable<IMessage> msgs = null;
    private static async Task RefreshOpenChannel()
    {

        var chan = await _client.GetChannelAsync(openChannelID) as IMessageChannel;
        if (chan == null)
        {
            Console.WriteLine("Channel is null!");
        }
        msgs = await chan.GetMessagesAsync(pullcnt).FlattenAsync();
        msgs = msgs.Reverse();
        await DrawOpenChannel();


    }
    // open the channel for viewing of the user
    private static async Task OpenChannel(IMessageChannel chan)
    {
        try
        {
            //update the openchannelid (static) so that everything knows that we have THIS channel open
            openChannelID = chan.Id;
            //dont send a null channel dipwit
            if (chan == null)
            {
                Console.WriteLine("Channel is null!");
                Environment.Exit(1);
            }
            await RefreshOpenChannel();

            // primary read and command processor loop
            // commands can be sent by simply typing it into the send box
            while (true)
            {
                // read a line
                msgtosend = "";
                var key = Console.ReadKey();
                msgtosend += key.KeyChar;
                await chan.TriggerTypingAsync();
                QuickDraw();
                while (true)
                {
                    key = Console.ReadKey();
                    Debug.Write(key.KeyChar);
                    if (key.KeyChar == '\n' || key.KeyChar == '\r')
                    {
                        if (!string.IsNullOrEmpty(msgtosend))
                            break;
                    }
                    if (key.Key == ConsoleKey.Backspace && !string.IsNullOrEmpty(msgtosend))
                    {
                        msgtosend = msgtosend[..^1];
                        QuickDraw();
                    }
                    else
                        msgtosend += key.KeyChar;
                }
                msgtosend = msgtosend.Replace("\\n", "\n");
                msgtosend = msgtosend.Replace("\b", "");
                if (msgtosend.StartsWith("clip "))
                {
                    var messages = msgs.ToList();
                    int toclip = -1;
                    string arg1 = msgtosend.Substring(5);
                    if (int.TryParse(arg1, out toclip))
                    {
                        if (messages[toclip] != null)
                        {
                            Console.Clear();
                            Console.WriteLine($"TimbaClip - <@{messages[toclip].Author.Id}>{messages[toclip].Content}");
                            Thread.Sleep(6000);
                            await DrawOpenChannel();

                        }
                        else
                        {
                            Console.WriteLine("nullptr");
                        }
                    }
                    else
                    {
                        Console.WriteLine("INVALID INT");
                    }
                }
                //delete by index
                if (msgtosend.StartsWith("del ") || msgtosend.StartsWith("rm "))
                {
                    await Task.Run(async () =>
                    {
                        // fixed. uses RPLY logic
                        var messages = msgs.ToList();
                        int todelete = -1;
                        string arg1 = "null";
                        if (msgtosend.StartsWith("del "))
                            arg1 = msgtosend.Substring(4);
                        else
                            arg1 = msgtosend.Substring(3);
                        if (int.TryParse(arg1, out todelete))
                        {
                            if (messages[todelete] != null)
                            {
                                try
                                {
                                    Console.Clear();
                                    Console.WriteLine($"Delete? ({messages[todelete].Content}");
                                    var ynstring = Console.ReadLine().ToLower();
                                    bool yn = ynstring == "yes" || ynstring == "y";
                                    if (yn)
                                    {
                                        await messages[todelete].DeleteAsync();
                                        await RefreshOpenChannel();
                                    }
                                    else
                                    {
                                        Console.WriteLine("Canceled");
                                        Thread.Sleep(3000);
                                        await DrawOpenChannel();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Could not delete message! " + ex.Message);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("INVALID INT");
                        }

                    });
                }
                // go back to selector
                else if (msgtosend.StartsWith("backout") || msgtosend == "cd ..")
                {
                    openChannelID = 0;
                    _ = OpenSelector();
                    return;
                }
                // refresh
                else if (msgtosend.StartsWith("ref"))
                {
                    await RefreshOpenChannel();
                }
                //OHSHIT
                else if (msgtosend == "p")
                {
                    Console.Clear();
                    Console.WriteLine("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n");
                    Environment.Exit(0);
                }
                else if (msgtosend.ToLower().StartsWith("setpassword "))
                {
                    //encrypt token.txt and set the first three chars to PWD.
                    //then the rest of the bytes should be base64 of the encrypted discord bot token.
                    //use AES256 with salt and a key from a SHA256 hash
                    byte[] enckey = SHA256.HashData(Encoding.UTF8.GetBytes(msgtosend.Substring(12)));
                    PrintBytes(enckey);
                    Console.WriteLine("Returned");
                    string intext = File.ReadAllText("token.txt");
                    using Aes aes = Aes.Create();
                    Console.WriteLine("Created");
                    aes.Key = enckey;
                    Console.WriteLine("set key");
                    aes.GenerateIV();

                    PrintBytes(aes.IV);

                    using MemoryStream ms = new();
                    using (CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        byte[] input = Encoding.UTF8.GetBytes(intext);
                        cs.Write(input, 0, input.Length);
                        cs.FlushFinalBlock();


                        // Output = IV + encrypted data, then Base64
                        byte[] result = new byte[aes.IV.Length + ms.Length];
                        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                        Buffer.BlockCopy(ms.ToArray(), 0, result, aes.IV.Length, (int)ms.Length);
                        PrintBytes(result);
                        string base64 = Convert.ToBase64String(result);
                        File.WriteAllText("token.txt", "PWD." + base64);
                    }
                }
                //update the amount of messages to pull every time
                else if (msgtosend.StartsWith("pull "))
                {
                    string arg = msgtosend.Substring(5);
                    if (int.TryParse(arg, out int intarg))
                    {
                        pullcnt = intarg;
                        Console.WriteLine("Updated to " + intarg);
                        await RefreshOpenChannel();
                    }
                    else
                    {
                        Console.WriteLine("INVALID INT");
                    }
                }
                else if (msgtosend.StartsWith("lsusers"))
                {
                    var guilds = _client.Guilds;
                    List<string> output = new List<string>();
                    foreach (var guild in guilds)
                    {
                        var users = guild.Users;

                        Console.WriteLine($"Getting {guild.Name}");
                        foreach (var user in users)
                        {
                            if (user.Activities.FirstOrDefault(a => a.Type == ActivityType.CustomStatus) is CustomStatusGame customStatus)
                            {
                                var outline = $" - {user.Username} {user.Status} {customStatus.State}";
                                if (!output.Contains(outline))
                                    output.Add(outline);
                            }
                            {
                                var outline = $" - {user.Username} {user.Status}";
                                if (!output.Contains(outline))
                                    output.Add(outline);
                            }
                        }
                    }
                    Console.WriteLine($"{string.Join("\n", output)}");

                }
                else if (msgtosend.ToLower().StartsWith("setStatus".ToLower()))
                {
                    string arg = msgtosend.Substring(10);
                    await _client.SetGameAsync(arg);
                    conf.Status = arg;
                    conf.Save();
                }
                else if (msgtosend.StartsWith("rply"))
                {
                    //get the number and the message
                    var arg1 = msgtosend.Substring(5);
                    //get the number from arg 1 then get the message from arg 1
                    var arg2 = arg1.Substring(arg1.IndexOf(" ") + 1);
                    var arg1num = arg1.Substring(0, arg1.IndexOf(" "));
                    var msgslist = msgs.ToList();
                    if (int.TryParse(arg1num, out int intarg))
                    {
                        if (msgslist[intarg] != null)
                        {
                            try
                            {
                                //await msgslist[intarg];
                                await chan.SendMessageAsync(arg2, messageReference: new MessageReference(msgslist[intarg].Id));
                                await RefreshOpenChannel();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Could not reply to message! " + ex.Message);
                            }
                        }
                    }

                    else
                    {
                        Console.WriteLine("INVALID INT");
                    }
                }
                else if (msgtosend.StartsWith("fm"))
                {
                    //get the last.fm username and api key from the config file
                    Console.WriteLine("FM RUNNING!");
                    var username = conf.FMUsername;
                    var apiKey = conf.FMApiKey;
                    using var httpClient = new HttpClient();
                    var trackInfo = await GetCurrentlyPlayingTrackAsync(httpClient, username, apiKey);
                    if (trackInfo != null)
                    {
                        var embedmaker = new Discord.EmbedBuilder()
                        {
                            Title = "Now Playing",
                            Description = $"{trackInfo.Artist} - {trackInfo.Name}",
                            Color = Discord.Color.Blue
                        };
                        await chan.SendMessageAsync(embed: embedmaker.Build());
                        await RefreshOpenChannel();
                    }
                    else
                    {
                        await chan.SendMessageAsync($"No track is currently playing for {username}.");
                        await RefreshOpenChannel();
                    }
                }
                else if (msgtosend.StartsWith("help"))
                {
                    Console.WriteLine("Commands:");
                    Console.WriteLine("del | rm <index> - delete a message by index");
                    Console.WriteLine("backout | cd .. - go back to channel selector");
                    Console.WriteLine("pull <number> - set the number of messages to pull");
                    Console.WriteLine("lsusers - list all users and their statuses");
                    Console.WriteLine("setStatus <status> - set the bot's status");
                    Console.WriteLine("rply <index> <message> - reply to a message by index");
                    Console.WriteLine($"fm - get the currently playing track from last.fm as {conf.FMUsername} (config.json)");
                }
                else
                    await Task.Run(() => { chan.SendMessageAsync(msgtosend); });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Encountered a problem in OPENCHANNEL. Restarting function!\n{ex.Message}");
            Thread.Sleep(4000);
            await OpenSelector();
        }
    }
    // logger
    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
    //ai API requests
    static async Task<TrackInfo?> GetCurrentlyPlayingTrackAsync(
    HttpClient httpClient,
    string username,
    string apiKey)
    {
        var requestUrl =
            "https://ws.audioscrobbler.com/2.0/?" +
            "method=user.getrecenttracks" +
            $"&user={Uri.EscapeDataString(username)}" +
            $"&api_key={Uri.EscapeDataString(apiKey)}" +
            "&format=json" +
            "&limit=1";

        using var response = await httpClient.GetAsync(requestUrl);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out _))
            return null;

        if (!root.TryGetProperty("recenttracks", out var recentTracks) ||
            !recentTracks.TryGetProperty("track", out var tracks))
            return null;

        var track = tracks.ValueKind switch
        {
            JsonValueKind.Array when tracks.GetArrayLength() > 0 => tracks[0],
            JsonValueKind.Object => tracks,
            _ => default
        };

        if (track.ValueKind != JsonValueKind.Object)
            return null;

        var name = track.TryGetProperty("name", out var nameElement)
            ? WebUtility.HtmlDecode(nameElement.GetString())
            : null;

        string? artist = null;

        if (track.TryGetProperty("artist", out var artistElement))
        {
            if (artistElement.ValueKind == JsonValueKind.String)
            {
                artist = WebUtility.HtmlDecode(artistElement.GetString());
            }
            else if (artistElement.ValueKind == JsonValueKind.Object &&
                     artistElement.TryGetProperty("#text", out var artistText))
            {
                artist = WebUtility.HtmlDecode(artistText.GetString());
            }
        }

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(artist))
            return null;

        var isNowPlaying =
            track.TryGetProperty("@attr", out var attributes) &&
            attributes.TryGetProperty("nowplaying", out var nowPlaying) &&
            string.Equals(
                nowPlaying.GetString(),
                "true",
                StringComparison.OrdinalIgnoreCase);

        if (!isNowPlaying)
            return null;

        return new TrackInfo(name, artist);
    }

    public class TrackInfo
    {
        public TrackInfo(string name, string artist)
        {
            Name = name;
            Artist = artist;
        }

        public string Name { get; }
        public string Artist { get; }
    }
    static void PrintObject(object? obj, string name)
    {
        Console.WriteLine("RUNNING");
        if (obj == null)
        {
            Console.WriteLine($"{name}: null");
            return;
        }

        var type = obj.GetType();

        foreach (var prop in type.GetProperties())
        {
            object? value;

            try
            {
                value = prop.GetValue(obj);
            }
            catch
            {
                Console.WriteLine($"{name}.{prop.Name}: <error>");
                continue;
            }

            if (value is System.Collections.IEnumerable enumerable &&
                value is not string)
            {
                int i = 0;

                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        Console.WriteLine($"{name}.{prop.Name}[{i}]: null");
                    }
                    else if (item.GetType().IsPrimitive ||
                             item is string ||
                             item is decimal ||
                             item is DateTime ||
                             item is DateTimeOffset)
                    {
                        Console.WriteLine($"{name}.{prop.Name}[{i}]: {item}");
                    }
                    else
                    {
                        PrintObject(item, $"{name}.{prop.Name}[{i}]");
                    }

                    i++;
                }

                if (i == 0)
                    Console.WriteLine($"{name}.{prop.Name}: []");

                continue;
            }

            if (value != null &&
                !prop.PropertyType.IsPrimitive &&
                prop.PropertyType != typeof(string) &&
                prop.PropertyType != typeof(decimal) &&
                prop.PropertyType != typeof(DateTime) &&
                prop.PropertyType != typeof(DateTimeOffset) &&
                !prop.PropertyType.IsEnum)
            {
                PrintObject(value, $"{name}.{prop.Name}");
            }
            else
            {
                Console.WriteLine($"{name}.{prop.Name}: {value}");
            }
        }
    }
    static void PrintBytes(byte[] bytetoprint)
    {
        foreach (var bytee in bytetoprint)
        {
            //print the 1s and 0s of the byte in binary
            var str = Convert.ToString(bytee, 2).PadLeft(8, '0');
            Console.Write(str);
            Thread.Sleep(1);
        }
        Console.WriteLine($"\n{Convert.ToHexString(bytetoprint)}");
    }
}
