﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace TelnetChatServer {
    public class DisconnectedException : Exception {
    }

    internal static class VT100 {
        public static string ESC = "\x1b";
        public static string CursorUp = "\x1b[1A";
        public static string EraseToStartOfLine = "\x1b[1K";
        public static string EraseScreen = "\x1b[2J";
    }

    class Peer {
        public int PeerId;
        public string Name;
        public bool Connected = false;
        public int CurrentId = -1;
        public AsyncStreamWriter Output = null;
        public AsyncStreamReader Input = null;

        public override string ToString () {
            if (Name != null)
                return Name;
            else
                return String.Format("Peer{0}", PeerId);
        }
    }

    struct Message {
        public Peer From;
        public string Text;
    }

    static class Program {
        static TaskScheduler Scheduler = new TaskScheduler(true);
        static List<Message> Messages = new List<Message>();
        static List<Peer> Peers = new List<Peer>();
        static BlockingQueue<Message> NewMessages = new BlockingQueue<Message>();
        static Future WaitingForMessages = null;
        const int MaxMessagesToDispatch = 100;
        static IEnumerator<object> _Dispatcher;

        static void DispatchNewMessage (Peer from, string message) {
            Messages.Add(new Message { From = from, Text = message });
            Future waitingFuture = Interlocked.Exchange<Future>(ref WaitingForMessages, null);
            if (waitingFuture != null) {
                try {
                    waitingFuture.Complete();
                } catch (FutureAlreadyHasResultException) {
                }
            }
        }

        static IEnumerator<object> MessageDispatcher () {
            while (true) {
                var waitList = new List<Future>();
                var waitingPeers = new List<Peer>();

                bool moreWork;
                do {
                    moreWork = false;
                    int newestId = Messages.Count - 1;
                    foreach (Peer peer in Peers.ToArray()) {
                        if (!peer.Connected)
                            continue;

                        if (peer.CurrentId != newestId) {
                            if ((newestId - peer.CurrentId) > MaxMessagesToDispatch)
                                peer.CurrentId = newestId - MaxMessagesToDispatch;

                            string text = null;
                            try {
                                Message message = Messages[peer.CurrentId + 1];

                                if (message.From == peer) {
                                    peer.CurrentId += 1;
                                    continue;
                                } else if (message.From != null) {
                                    text = String.Format("<{0}> {1}", message.From, message.Text);
                                } else {
                                    text = String.Format("*** {0}", message.Text);
                                }

                            } catch {
                                continue;
                            }

                            Future f = null;
                            f = peer.Output.PendingOperation;
                            if (f == null) {
                                f = peer.Output.WriteLine(text);
                                if (f.CheckForFailure(typeof(BufferFullException))) {
                                    Console.WriteLine("Send buffer for peer {0} full", peer);
                                    continue;
                                }

                                f.RegisterOnComplete((r, e) => {
                                    if ((e is DisconnectedException) || (e is IOException) || (e is SocketException) || (e is FutureDisposedException)) {
                                        Scheduler.QueueWorkItem(() => {
                                            PeerDisconnected(peer, new Future(e));
                                        });
                                    }
                                });
                                peer.CurrentId += 1;
                            } else {
                                waitList.Add(f);
                                waitingPeers.Add(peer);
                                continue;
                            }
                        }

                        if (peer.CurrentId != newestId)
                            moreWork = true;
                    }
                } while (moreWork);

                if (waitList.Count > 0)
                    System.Diagnostics.Debug.WriteLine(String.Format("Waiting on peers: {0}", waitingPeers.ToArray()));

                Future waitForNewMessage = new Future();
                Future oldFuture = Interlocked.Exchange<Future>(ref WaitingForMessages, waitForNewMessage);
                if (oldFuture != null) {
                    oldFuture.RegisterOnComplete((r, e) => { waitForNewMessage.SetResult(r, e); });
                }
                waitList.Add(waitForNewMessage);
                yield return new WaitForFirst(waitList);
            }
        }

        static void PeerConnected (Peer peer) {
            if (peer.Connected)
                return;
            peer.Connected = true;
            Console.WriteLine("User {0} has connected", peer);
            DispatchNewMessage(null, String.Format("{0} has joined the chat", peer));
            Peers.Add(peer);
        }

        static void PeerDisconnected (Peer peer, Future future) {
            if (!peer.Connected)
                return;
            Peers.Remove(peer);
            peer.Connected = false;
            object r;
            Exception ex;
            future.GetResult(out r, out ex);
            Console.WriteLine("User {0} has disconnected", peer);
            DispatchNewMessage(null, String.Format("{0} has left the chat", peer));
        }

        static IEnumerator<object> PeerTask (TcpClient client, Peer peer) {
            var input = new AsyncStreamReader(new AwesomeStream(client.Client, false), Encoding.ASCII);
            var output = new AsyncStreamWriter(new AwesomeStream(client.Client, false), Encoding.ASCII);
            peer.Input = input;
            peer.Output = output;

            yield return output.WriteLine("Welcome! Please enter your name.");
            Future f = input.ReadLine();
            yield return f;
            if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                PeerDisconnected(peer, f);
                yield break;
            }
            peer.Name = f.Result as string;

            PeerConnected(peer);

            yield return output.Write(VT100.EraseScreen);
            
            while (peer.Connected) {
                f = input.ReadLine();
                yield return f;
                string nextLineText = null;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    PeerDisconnected(peer, f);
                    yield break;
                }
                nextLineText = f.Result as string;

                if ((nextLineText != null) && (nextLineText.Length > 0)) {
                    DispatchNewMessage(peer, nextLineText);
                }
            }
        }

        static IEnumerator<object> AcceptConnectionsTask (TcpListener server) {
            server.Start();
            try {
                int nextId = 0;
                while (true) {
                    Future connection = server.AcceptIncomingConnection();
                    yield return connection;

                    Peer peer = new Peer { PeerId = nextId++ };
                    TcpClient client = connection.Result as TcpClient;
                    client.Client.Blocking = false;
                    client.Client.NoDelay = true;
                    var peerTask = PeerTask(client, peer);
                    Scheduler.Start(peerTask, TaskExecutionPolicy.RunAsBackgroundTask);
                }
            } finally {
                server.Stop();
            }
        }
        
        static void Main (string[] args) {
            ThreadPool.SetMaxThreads(100, 1000);
            TcpListener server = new TcpListener(System.Net.IPAddress.Any, 1234);
            Scheduler.Start(AcceptConnectionsTask(server), TaskExecutionPolicy.RunAsBackgroundTask);
            _Dispatcher = MessageDispatcher();
            Scheduler.Start(_Dispatcher, TaskExecutionPolicy.RunAsBackgroundTask);

            Console.WriteLine("Ready for connections.");

            try {
                while (true) {
                    Scheduler.Step();
                    Scheduler.WaitForWorkItems();
                }
            } catch (Exception ex) {
                Console.WriteLine("Unhandled exception: {0}", ex);
                Console.ReadLine();
            }
        }
    }
}