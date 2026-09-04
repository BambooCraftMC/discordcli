using Discord;
using Discord.WebSocket;
using Microsoft.VisualBasic;
using System.Runtime.InteropServices;
using System.Text;
class Program
{
    private static int pullcnt = 3;
    private static DiscordSocketClient _client;
    public static List<(string, ulong)> allChannels = new List<(string, ulong)>();
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.WriteLine("TOKEN:");
        string token = "";
        if (File.Exists("token.txt"))
        {
            token = File.ReadAllText("token.txt");
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

        // login with the token
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        _client.Ready += () =>
        {
            Console.WriteLine("READY!");
            //list all channels
            //and populate channel list for rest of runtime
            var guilds = _client.Guilds;
            foreach (var guild in guilds)
            {
                Console.WriteLine(guild.Name);
                //list all channels
                var channels = guild.Channels;
                foreach (var channel in channels)
                {
                    Console.WriteLine($"-  {channel.Name}({channel.Id})");
                    allChannels.Add((channel.Name, channel.Id));
                }
                var users = guild.Users;
                foreach (var user in users)
                {
                    Console.WriteLine($"- USR.{user.Username}");
                    if (!allChannels.Contains(($"USR.{user.Username}", user.Id)))
                        allChannels.Add(($"USR.{user.Username}", user.Id));
                }


            }
            _ = OpenSelector();
            return Task.CompletedTask;
        };
        await Task.Delay(-1);
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
                if (allChannels[selected].Item1.StartsWith("USR"))
                {
                    var chan = _client.GetUserAsync(channelid).GetAwaiter().GetResult().CreateDMChannelAsync().GetAwaiter().GetResult() as IMessageChannel;
                    _ = OpenChannel(chan);
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
                Console.WriteLine($"> {channel.Item1}({channel.Item2})");
                Console.BackgroundColor = ConsoleColor.Black;
            }
            else
            {
                Console.WriteLine($"  {channel.Item1}({channel.Item2})");
            }
        }
    }
    private static ulong openChannelID = 0;
    private static async Task MessageRecived(SocketMessage imsg)
    {
        //if channel is open then refresh the open channel
        if (openChannelID != 0 && imsg.Channel.Id == openChannelID)
        
            RefreshOpenChannel();
    }
    // refresh the open channel so that its populated with new messages
    private static async Task RefreshOpenChannel()
    {
        
            var chan = await _client.GetChannelAsync(openChannelID) as IMessageChannel;
            if (chan == null)
            {
                Console.WriteLine("Channel is null!");
            }
            var msgs = await chan.GetMessagesAsync(pullcnt).FlattenAsync();
            msgs = msgs.Reverse();
            Console.Clear();
            Console.WriteLine($"   >>{chan.Name}<<");
            int i = 0;
            foreach (var msg in msgs)
            {
                if (msg.Reference != null)
            {
                var refmsg = await chan.GetMessageAsync(msg.Reference.MessageId.Value);
                Console.WriteLine($"[{i}]<{msg.Timestamp.LocalDateTime.Hour}:{msg.Timestamp.LocalDateTime.Minute}>({msg.Author.Username}){msg.Author.GlobalName}<@{msg.Author.Id}> >> {msg.Content} (reply to: {refmsg.Author.Username} >> {refmsg.Content}) {string.Join(", ", msg.Attachments.Select(e => e.Url))}");
            }
            else
                Console.WriteLine($"[{i}]<{msg.Timestamp.LocalDateTime.Hour}:{msg.Timestamp.LocalDateTime.Minute}>({msg.Author.Username}){msg.Author.GlobalName}<@{msg.Author.Id}> >> {msg.Content} {string.Join(", ", msg.Attachments.Select(e => e.Url))}");

                i++;
            }
        
    }
    // open the channel for viewing of the user
    private static async Task OpenChannel(IMessageChannel chan)
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
            var msgtosend = Console.ReadLine();
            msgtosend = msgtosend.Replace("\\n", "\n");
            //delete by index
            if (msgtosend.StartsWith("del "))
            {
                await Task.Run(async () => {
                    var Enumessages = await chan.GetMessagesAsync(100).FlattenAsync();
                    var messages = Enumessages.ToList();
                    messages.Reverse();
                    int todelete = -1;
                    if (int.TryParse(msgtosend.Substring(4), out todelete))
                    {
                    if (messages[todelete] != null)
                        {
                            try
                            {
                                
                                await messages[todelete].DeleteAsync();
                                await RefreshOpenChannel();
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
            //update the amount of messages to pull every time
            else if (msgtosend.StartsWith("pull ")) 
            {
                string arg = msgtosend.Substring(5);
                if (int.TryParse(arg, out int intarg))
                {
                    pullcnt = intarg;
                    Console.WriteLine("Updated to "+ intarg);
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
                foreach (var guild in guilds)
                {
                    var users = guild.Users;
                    foreach (var user in users)
                    {
                        Console.WriteLine($" - {user.Username} {user.Status} {user.Activities.FirstOrDefault()?.Details}");
                    }
                }
            }
            else 
                await Task.Run(() => { chan.SendMessageAsync(msgtosend); });
        }
    }
    // logger
    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
}
