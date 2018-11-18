using SimpleServer.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SimpleServer
{
    class Program
    {

        static void Main(string[] args)
        {
            using (Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                //Set invironment
                string root = Environment.CurrentDirectory.Replace("\\", "/");
                root = root.Last() == '/' ? root.Substring(0, root.Length - 1) : root;
                ChangeTracker tracker = new ChangeTracker(root);

                //Open server
                int port = args.Length > 0 ? (Int32.TryParse(args[0], out int r) ? r : 1415) : 1415;
                server.Bind(new IPEndPoint(IPAddress.Any, port));
                server.Listen(100);
                Process.Start("chrome.exe", "127.0.0.1:" + port);
                Console.WriteLine("Server opend at 127.0.0.1:" + port);

                while (true)
                {
                    try
                    {
                        using (Socket client = server.Accept())
                        {
                            byte[] buf = new byte[1024];
                            int len = client.Receive(buf);
                            string line = Encoding.UTF8.GetString(buf, 0, len);
                            string fLine = line.Split('\n')[0]; //First line of request
                            string path = fLine.Split(' ')[1];   //Path of request

                            if (path.Contains("__auto_update_script"))
                            {
                                string time = path.Replace("/", "").Replace("__auto_update_script_", "").Trim(); //Last modify time of any file
                                if (tracker.IsAnyThingChanged(time)) Send(client, R("reloadScript"));               //If file changed, send reload script
                                else Send(client, "");                                                                                  //else do noting
                                continue;                                                                                                  //Close connection
                            }

                            path = path.Last() == '/' ? path + "index.html" : path; //If path is directory, path is index.html of that directory
                            Console.WriteLine("Requested path : " + path);

                            try
                            {
                                string fullPath = root + path;
                                FileInfo fileInfo = new FileInfo(fullPath);

                                //Inject injection script to head tag of any html file
                                if (fileInfo.Extension.Equals(".html"))
                                {
                                    using (StreamReader fr = new StreamReader(fullPath))
                                    {
                                        string html = fr.ReadToEnd();

                                        //Auto update script
                                        html = html.Replace("<head>", "<head>" + ("<script>" + R("injectionScript") + "</script>").Replace("TIME", tracker.LastModifiedTime));

                                        //Modifiy message div
                                        if (tracker.ModifyMessage.Length > 1) html = html.Replace("<body>", "<body>" + R("messageDiv").Replace("MESSAGE", tracker.ModifyMessage));
                                        tracker.ClearMessage();

                                        Send(client, html);
                                    }
                                }
                                else
                                {
                                    using (Stream sr = new FileStream(fullPath, FileMode.Open))
                                    {
                                        string header = "HTTP/1.1 200 OK\n" +
                                            "Content-Length : " + fileInfo.Length + "\n\n";
                                        client.Send(Encoding.UTF8.GetBytes(header));
                                        byte[] buf2 = new byte[1024];
                                        for (int len2 = 0; (len2 = sr.Read(buf2, 0, 1024)) > 0;) client.Send(buf2, len2, SocketFlags.None);
                                    }
                                }
                            }
                            catch
                            {
                                string message = "No such file : " + path;
                                string header = "HTTP/1.1 404 Not Found\n" +
                                    "Content-Length : " + message.Length + "\n\n";
                                client.Send(Encoding.UTF8.GetBytes(header));
                                client.Send(Encoding.UTF8.GetBytes(message));
                            }
                        }
                    }
                    catch
                    {
                        //Socket error
                    }
                }
            }
        }

        private static void Send(Socket client, string html)
        {
            string header = "HTTP/1.1 200 OK\n" +
    "Content-Length : " + html.Length + "\n\n";
            client.Send(Encoding.UTF8.GetBytes(header));
            client.Send(Encoding.UTF8.GetBytes(html));
        }

        private static string R(string key)
        {
            return Resources.ResourceManager.GetString(key);
        }
    }

    class ChangeTracker
    {
        private string lastModifiedTime = "None";
        private string modifyMessage = "";

        public string LastModifiedTime
        {
            get
            {
                return lastModifiedTime;
            }
        }

        public string ModifyMessage
        {
            get
            {
                return modifyMessage;
            }
        }

        public ChangeTracker(string root)
        {
            CheckFiles(root);
        }

        private void CheckFiles(string dir)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(dir);
            FileSystemEventHandler fileEvent = (object source, FileSystemEventArgs e) =>
            {
                if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
                string message = "File " + e.Name + " " + e.ChangeType.ToString().ToLower();
                Console.WriteLine(message);
                modifyMessage = message;
                lastModifiedTime = DateTime.Now.Ticks.ToString();
            };
            watcher.Created += fileEvent;
            watcher.Deleted += fileEvent;
            watcher.Changed += fileEvent;
            watcher.Renamed += (object source, RenamedEventArgs e) =>
            {
                string message = "File " + e.OldName + " renamed to " + e.Name;
                Console.WriteLine(message);
                lastModifiedTime = DateTime.Now.Ticks.ToString();
                modifyMessage = message;
            };
            watcher.EnableRaisingEvents = true;

            foreach (DirectoryInfo di in new DirectoryInfo(dir).GetDirectories()) CheckFiles(di.FullName);
        }

        public bool IsAnyThingChanged(string lastModified)
        {
            return !lastModifiedTime.Equals(lastModified);
        }

        internal void ClearMessage()
        {
            modifyMessage = "";
        }
    }
}
