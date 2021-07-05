using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Principal;

namespace StationServer
{
    class Program
    {
        class HttpServer
        {
            public static string port = "1234";
            public static string cookie = "";
            public static string quality = "64K";
            public static int timeout = 30000;
            public static HttpListener listener;
            private static Thread listenThread1;
            private static Thread listenThread2;
            private static List<string> Conversion = new List<string> { { "Conversion List"} }; 

            private static void MP3Convert(object s)
            {
                bool processed = false;
                HttpListenerContext ctx = s as HttpListenerContext;
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                string command = req.Url.ToString().Split(new string[] { "RUST:" }, StringSplitOptions.None)[1];
                string[] info = command.Split(new string[] { "?YT:" }, StringSplitOptions.None);
                Console.WriteLine(info[0] + " Requested " + info[1]);

                string mp3path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\" + info[1].Split('&')[0].Split(new string[] { "?v=" }, StringSplitOptions.None)[1] + ".mp3";

                if(Conversion.Contains(mp3path))
                {
                    Console.WriteLine("Waiting for Conversion");
                    int loops = 0;
                    while (Conversion.Contains(mp3path))
                    {
                        Thread.Sleep(1000);
                        if (loops > timeout / 1000)
                        {
                            Thread.Sleep(1000);
                            if (Conversion.Contains(mp3path))
                            {
                                Conversion.Remove(mp3path);
                            }
                            break;
                        }
                    }
                }


                if (!File.Exists(mp3path))
                {
                    Conversion.Add(mp3path);
                    Console.WriteLine("Downloading....");
                    Process p = new Process();
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\youtube-dl.exe";
                    startInfo.Arguments = @"--id --audio-quality "+ quality + " --cookies " + cookie +" -w -x --audio-format mp3 --ffmpeg-location " + Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + " " + info[1].Split('&')[0]; // cmd.exe spesific implementation
                    p.StartInfo = startInfo;
                    p.Start();
                    Task t = new Task(async () =>
                    {
                        await Task.Delay(timeout);
                        if (!processed)
                        {
                            Process[] runningProcesses = Process.GetProcessesByName("youtube-dl");
                            foreach (Process process in runningProcesses)
                            {
                                process.Kill();
                                Console.WriteLine("Killed Stalled Process");
                            }
                        }
                        return;
                    });
                    t.RunSynchronously();
                    p.WaitForExit();
                    t.Dispose();
                    processed = true;
                    if (Conversion.Contains(mp3path))
                    {
                        Conversion.Remove(mp3path);
                    }
                }

                if (File.Exists(mp3path))
                {
                    Console.WriteLine("Playing....");
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

                            resp.Close();
                            input.Close();
                        }
                        catch { }
                        try
                        {
                            resp.Abort();
                        }
                        catch { }
                    }
                }
                Console.WriteLine();
            }

            public static Task HandleIncomingConnections()
            {
                bool runServer = true;
                while (runServer)
                {
                    HttpListenerContext ctx = listener.GetContext();
                    Console.WriteLine("RequestIP: " + ctx.Request.RemoteEndPoint.ToString());
                        if ((ctx.Request.HttpMethod == "GET") && (ctx.Request.Url.ToString().Contains("RUST:")))
                        {
                            listenThread1 = new Thread(new ParameterizedThreadStart(MP3Convert));
                            listenThread1.Start(ctx);
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
                    listener.Prefixes.Add("http://*:"+ port +"/");
                    listener.Start();
                    Console.WriteLine("Listening for connections on port:"+port);

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
                if(args.Length > 0)
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
                    cookie = args[3];
                    Console.WriteLine("SET COOKIE:" + cookie);
                }

                if (!IsAdministrator())
                {
                    Console.WriteLine("Must Run As ADMIN to open Ports");
                    Console.ReadKey();
                    System.Environment.Exit(1);
                }

                listenThread2 = new Thread(new ParameterizedThreadStart(server));
                listenThread2.Start();
            }
        }
    }
}
