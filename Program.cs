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
            public static string port = "1234";
            public static string datafile = "";
            public static string cookie = "";
            public static string quality = "64K";
            public static int timeout = 60000;
            public static HttpListener listener;
            private static Thread ProcessThread;
            private static Thread listenThread;
            private static Dictionary<string,string> Conversion = new Dictionary<string, string> { { "Conversion List", "Status" } };

            private static void SendMessage(HttpListenerResponse resp, string msg)
            {
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

                string command = req.Url.ToString().Split(new string[] { "RUST:" }, StringSplitOptions.None)[1];
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
                else if (!command.Contains("?YT:"))
                {
                    resp.Abort();
                    return;
                }
                string[] info = command.Split(new string[] { "?YT:" }, StringSplitOptions.None);
                Console.WriteLine("RequestIP: " + ctx.Request.RemoteEndPoint.ToString());
                Console.WriteLine(info[0] + " Requested " + info[1]);

                if (datafile != "")
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
                            resp.Abort();
                            Console.WriteLine("Blocked Request!");
                            return;
                        }
                    }
                    else
                    {
                        resp.Abort();
                        Console.WriteLine("Datafile not found!");
                        return;
                    }
                }

                string mp3dir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string mp3path = mp3dir + "\\" + info[1].Split('&')[0].Split(new string[] { "?v=" }, StringSplitOptions.None)[1] + ".mp3";

                if (Conversion.ContainsKey(mp3path))
                {
                    int loops = 0;
                    while (Conversion.ContainsKey(mp3path))
                    {
                        Thread.Sleep(1000);
                        if (loops > timeout / 1000)
                        {
                            Thread.Sleep(1000);
                            if (Conversion.ContainsKey(mp3path))
                            {
                                Conversion.Remove(mp3path);
                            }
                            break;
                        }
                    }
                }


                if (!File.Exists(mp3path))
                {
                    Conversion.Add(mp3path,"Downloading");
                    Process p = new Process();
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\youtube-dl.exe";
                    if (cookie == "")
                    {
                        startInfo.Arguments = @"--id --ignore-errors --no-playlist --audio-quality " + quality + " -w -x --audio-format mp3 --ffmpeg-location " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + " " + info[1];
                    }
                    else
                    {
                        startInfo.Arguments = @"--id --ignore-errors --no-playlist --audio-quality " + quality + " --cookies " + cookie + " -w -x --audio-format mp3 --ffmpeg-location " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + " " + info[1]; 
                    }
                    
                    p.StartInfo = startInfo;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();

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
                    while (File.Exists(mp3path.Replace("mp3","m4a")) || File.Exists(mp3path.Replace("mp3", "webm")) || File.Exists(mp3path.Replace("mp3", "mp4")))
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

                    if (Conversion.ContainsKey(mp3path))
                    {
                        Conversion.Remove(mp3path);
                    }
                }

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
                bool runServer = true;
                while (runServer)
                {
                    HttpListenerContext ctx = listener.GetContext();
                    if ((ctx.Request.HttpMethod == "GET") && (ctx.Request.Url.ToString().Contains("RUST:")))
                    {
                        ProcessThread = new Thread(new ParameterizedThreadStart(MP3Convert));
                        ProcessThread.Start(ctx);
                    }
                    ctx = null;
                }
                return null;
            }

            private static void server(object s)
            {
                bool runServer = true;
                while (runServer)
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://*:" + port + "/");
                    listener.Start();
                    Console.WriteLine("Listening for connections on port:" + port);

                    Task listenTask = HandleIncomingConnections();
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

                if (!IsAdministrator())
                {
                    Console.WriteLine("Must Run As ADMIN to open Ports");
                    Console.ReadKey();
                    System.Environment.Exit(1);
                }

                listenThread = new Thread(new ParameterizedThreadStart(server));
                listenThread.Start();
            }
        }
    }
}
