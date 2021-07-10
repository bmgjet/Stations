using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Principal;
using Newtonsoft.Json;
using System.Text;

namespace StationServer
{
    class Program
    {
        class HttpServer
        {
            //server settings defaults
            public static string port = "1234";
            public static string datafile = "";
            public static string cookie = "";
            public static string quality = "64K";
            public static string extraargs = "";
            public static int timeout = 60000;
            //
            public static HttpListener listener;
            private static Thread ProcessThread;
            private static Thread listenThread;
            private static Dictionary<string, string> Conversion = new Dictionary<string, string> { { "Conversion List", "Status" } };

            private static void SendMessage(HttpListenerResponse resp, string msg)
            {
                //Sends text status messages
                byte[] data = Encoding.UTF8.GetBytes(msg);
                resp.ContentType = "text/html";
                resp.ContentEncoding = Encoding.UTF8;
                resp.ContentLength64 = data.LongLength;
                resp.OutputStream.WriteAsync(data, 0, data.Length);
                resp.Close();
                return;
            }
            private static void MP3Convert(object s)
            {
                bool processed = false;
                bool premission = false;
                HttpListenerContext ctx = s as HttpListenerContext;
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                //Split command out
                string command = req.Url.ToString().Split(new string[] { "RUST:" }, StringSplitOptions.None)[1];

                //Status Command
                if (command.Contains("?DL:"))
                {
                    string[] Status = command.Split(new string[] { "?DL:" }, StringSplitOptions.None);
                    string filename = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\" + Status[1].Split('&')[0].Split(new string[] { "?v=" }, StringSplitOptions.None)[1] + ".mp3";
                    if (Conversion.ContainsKey(filename))
                    {
                        SendMessage(resp, Conversion[filename]);
                        return;
                    }
                    else if (File.Exists(filename))
                    {
                        SendMessage(resp, "Ready To Play");
                        return;
                    }
                    SendMessage(resp, "No File");
                    return;
                }
                //If not Status or Youtube Command abort connection
                else if (!command.Contains("?YT:"))
                {
                    resp.Abort();
                    return;
                }
                string[] info = command.Split(new string[] { "?YT:" }, StringSplitOptions.None);
                Console.WriteLine("RequestIP: " + ctx.Request.RemoteEndPoint.ToString());
                Console.WriteLine(info[0] + " Requested " + info[1]);

                //Some light server secuirty to check that user has used command in game.
                if (datafile != "") //If Datafile is set then do security other wise disable.
                {
                    if (File.Exists(datafile))
                    {
                        using (var file = File.Open(datafile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            byte[] temp = new byte[file.Length];
                            file.Read(temp, 0, (int)file.Length);
                            var str = System.Text.Encoding.Default.GetString(temp);
                            JsonTextReader reader = new JsonTextReader(new StringReader(str));
                            while (reader.Read())
                            {
                                try
                                {
                                    //Checks Rust Server Data UserID against Requests UserID.
                                    if ((string)reader.Value == info[0])
                                    {
                                        premission = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                            file.Close();
                        }
                        if (!premission)
                        {
                            //Block request since user isnt in data file.
                            resp.Abort();
                            Console.WriteLine("Blocked Request!");
                            return;
                        }
                    }
                    else
                    {
                        //Security set but couldnt find datafile to check userid against.
                        resp.Abort();
                        Console.WriteLine("Datafile not found!");
                        return;
                    }
                }

                string mp3dir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string mp3path = mp3dir + "\\" + info[1].Split('&')[0].Split(new string[] { "?v=" }, StringSplitOptions.None)[1] + ".mp3";

                //Some one else is already download/convert file so pause this request.
                if (Conversion.ContainsKey(mp3path))
                {
                    int loops = 0;
                    while (Conversion.ContainsKey(mp3path))
                    {
                        Thread.Sleep(1000);
                        //The Already download/convert request timed out so remove from list.
                        if (loops > timeout)
                        {
                            Thread.Sleep(1000);
                            if (Conversion.ContainsKey(mp3path))
                            {
                                Conversion.Remove(mp3path);
                            }
                            break;
                        }
                        loops += 1000;
                    }
                }

                //If mp3 isnt already on the server download/convert other wise go straight to playing it.
                if (!File.Exists(mp3path))
                {
                    Conversion.Add(mp3path, "Downloading");
                    Process p = new Process();
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\youtube-dl.exe";
                    if (cookie == "")
                    {
                        startInfo.Arguments = @"--id " + extraargs + "--ignore-errors --no-playlist --audio-quality " + quality + " -w -x --audio-format mp3 --ffmpeg-location " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + " " + info[1];
                    }
                    else
                    {
                        //Cookie txt file set to use that to login to youtube.
                        startInfo.Arguments = @"--id " + extraargs + "--ignore-errors --no-playlist --audio-quality " + quality + " --cookies " + cookie + " -w -x --audio-format mp3 --ffmpeg-location " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + " " + info[1];
                    }

                    //Open Youtube-DL console
                    p.StartInfo = startInfo;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();

                    //Read from youtube-dl output
                    Task output = new Task(() =>
                    {
                        while (!processed)
                        {
                            string consoleout = p.StandardOutput.ReadLine();
                            if (consoleout != null)
                            {
                                if (consoleout.Contains("[ffmpeg]"))
                                {
                                    if (Conversion.ContainsKey(mp3path))
                                        Conversion[mp3path] = "Converting";
                                    processed = true;
                                }
                                if (consoleout.Contains("[download]"))
                                {
                                    if (Conversion.ContainsKey(mp3path))
                                        Conversion[mp3path] = "Downloading " + consoleout.Split('%')[0].Substring(11) + "%";
                                }
                                Console.WriteLine(Conversion[mp3path]);
                            }

                        }
                        return;
                    });
                    output.RunSynchronously();

                    //Run timeout check on download.
                    int waited = 0;
                    while (!File.Exists(mp3path))
                    {
                        Thread.Sleep(100);
                        waited += 100;
                        if (waited > timeout)
                        {
                            Console.WriteLine("Killed Stalled Process");
                            resp.Close();
                            resp.Abort();
                            return;
                        }
                    }
                    output.Dispose();

                    //Run timeout check on encoding.
                    while (File.Exists(mp3path.Replace("mp3", "m4a")) || File.Exists(mp3path.Replace("mp3", "webm")) || File.Exists(mp3path.Replace("mp3", "mp4")))
                    {
                        Thread.Sleep(100);
                        waited += 100;
                        if (waited > timeout)
                        {
                            Console.WriteLine("Killed Stalled Process");
                            resp.Close();
                            resp.Abort();
                            return;
                        }
                    }

                    //Remove from conversion list.
                    if (Conversion.ContainsKey(mp3path))
                    {
                        Conversion.Remove(mp3path);
                    }
                }

                //Mp3 Exsists on serer so send data to requested client.
                if (File.Exists(mp3path))
                {
                    using (var input = File.Open(mp3path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        resp.ContentType = "audio/mpeg";
                        resp.ContentLength64 = input.Length;
                        resp.AddHeader("Date", DateTime.Now.ToString("r"));
                        resp.AddHeader("Last-Modified", System.IO.File.GetLastWriteTime(mp3path).ToString("r"));

                        byte[] buffer = new byte[1024 * 16];
                        int nbytes;

                        try
                        {
                            while ((nbytes = input.Read(buffer, 0, buffer.Length)) > 0)
                                resp.OutputStream.Write(buffer, 0, nbytes);

                        }
                        catch { }
                        resp.Close();
                        input.Close();

                    }
                }
            }

            public static Task HandleIncomingConnections()
            {
                while (true)
                {
                    HttpListenerContext ctx = listener.GetContext();
                    //Check is a GET request and its in RUST: command.
                    if ((ctx.Request.HttpMethod == "GET") && (ctx.Request.Url.ToString().Contains("RUST:")))
                    {
                        //Spawn a thread to handle request.
                        ProcessThread = new Thread(new ParameterizedThreadStart(MP3Convert));
                        ProcessThread.Start(ctx);
                    }
                    ctx = null;
                }
            }

            private static void server(object s)
            {
                //Loop listen thread incase it crashes
                while (true)
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://*:" + port + "/");
                    listener.Start();
                    Console.WriteLine("Listening for connections on port:" + port);

                    Task listenTask = HandleIncomingConnections();
                    //Wait for a connection
                    listenTask.GetAwaiter().GetResult();
                    listener.Close();
                }
            }

            public static bool IsAdministrator()
            {
                return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                       .IsInRole(WindowsBuiltInRole.Administrator);
            }

            public static void Main(string[] args)
            {
                //Program Entrance

                //Read Settings Passed in Startup
                if (args.Length > 0)
                {
                    port = args[0];
                    Console.WriteLine("SET PORT:" + port);
                }
                if (args.Length > 1)
                {
                    quality = args[1];
                    Console.WriteLine("SET QUALITY:" + quality);
                }
                if (args.Length > 2)
                {
                    timeout = int.Parse(args[2]);
                    Console.WriteLine("Set Timout:" + timeout.ToString() + "ms");
                }
                if (args.Length > 3)
                {
                    datafile = args[3];
                    Console.WriteLine("SET DATAFILE:" + datafile);
                }
                if (args.Length > 4)
                {
                    cookie = args[4];
                    Console.WriteLine("SET COOKIE:" + cookie);
                }

                if (args.Length > 5)
                {
                    extraargs = args[5];
                    Console.WriteLine("SET Extra Args:" + extraargs);
                }

                //Check if admin since you need that to open ports on win10
                if (!IsAdministrator())
                {
                    Console.WriteLine("Must Run As ADMIN to open Ports");
                    Console.ReadKey();
                    System.Environment.Exit(1);
                }

                //Start listening Thread
                listenThread = new Thread(new ParameterizedThreadStart(server));
                listenThread.Start();
            }
        }
    }
}
