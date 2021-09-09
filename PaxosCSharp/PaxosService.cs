using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static PaxosCSharp.IPaxosService;

namespace PaxosCSharp
{
    interface IPaxosService 
    {
        public record CheatProposer();
        public record GetRound();

        public record Prepare(int round, int value = 0);
        public record Promise(int round, int value);
        public record Accept(int round, int result = 0);
        public record Accepted(int round, int result);
    }

    public class PaxosService : ReceiveActor
    {
        private ActorSystem _actorSystem { get; set; }
        private List<ActorSelection> knownPeers = new List<ActorSelection>();

        bool isProposer = false;
        int lastRound = 0;
        List<int> votes = new List<int>();
        int acceptedCounter = 0;

        bool pendingAcceptor = false;
        int value;

        record Handshake();
        record Message(string value);

        public PaxosService(ActorSystem actorSystem)
        {
            _actorSystem = actorSystem;
            
            var parsePort = Regex.Match(_actorSystem.Settings.Config.ToString(), "port\\D+(\\d+)").Groups[1].Value;
            int localPort;
            int.TryParse(parsePort, out localPort);

            for (int checkPort = 8001; checkPort <= 8005; checkPort++)
            {
                if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners().Any(l => l.Port == checkPort))
                {
                    if (checkPort == localPort)
                        continue;

                    var newPeer = _actorSystem.ActorSelection($"akka.tcp://PaxosSystem@localhost:{checkPort}/user/PaxosService");
                    knownPeers.Add(newPeer);
                    newPeer.Tell(new Handshake());
                }
            }

            Receive<Handshake>(x => knownPeers.Add(_actorSystem.ActorSelection(Sender.Path)));
            Receive<string>(message => BroadcastTo(new Message(message)));

            Receive<Message>(message => { 
                if (Self != Sender)
                    Console.WriteLine($"[{GetPeerName(Sender)}]: {message.value}"); 
            });

            //Cheat
            Receive<CheatProposer>(x => {
                isProposer = true;
                Console.WriteLine("* You became Proposer!");
            });
            Receive<GetRound>(x => Console.WriteLine($"* This Round {lastRound}"));

            //Paxos
            Receive<Prepare>(prepare => {
                if(isProposer)
                {
                    votes.Add(prepare.value);
                    lastRound = prepare.round > lastRound ? prepare.round : lastRound++;
                    BroadcastTo(new Prepare(lastRound));
                }
                else
                {
                    if (!pendingAcceptor)
                    {
                        int val = 0;
                        if (prepare.round >= lastRound)
                        {
                            pendingAcceptor = true;
                            lastRound = prepare.round;
                            Console.WriteLine($"Proposer {GetPeerName(Sender)} organized a new round {lastRound}. Press 'ENTER' and input value: ");
                            val = Convert.ToInt32(Console.ReadLine());
                            Console.WriteLine($"Value {val}. Vote accepted!");
                        }
                        Sender.Tell(new Promise(lastRound, val));
                    }
                }
            });

            Receive<Promise>(promise => {
                if(isProposer)
                {
                    if (promise.round <= lastRound)
                    {
                        if (promise.round == lastRound)
                        {
                            votes.Add(promise.value);
                            Console.WriteLine($"{GetPeerName(Sender)} voted for value {promise.value}!");
                        }
                    }
                    else
                    {
                        CloseRound();
                        lastRound = promise.round;
                        BroadcastTo(new Prepare(lastRound++));
                    }
                }
            });

            Receive<Accept>(accept => { 
                if(isProposer)
                {
                    var res = votes.GroupBy(s => s).OrderByDescending(g => g.Count()).First().Key;
                    BroadcastTo(new Accept(lastRound, res));
                    value = res;
                }
                else
                {
                    pendingAcceptor = false;
                    value = accept.result;
                    Sender.Tell(new Accepted(accept.round, value));
                }
            });

            Receive<Accepted>(accepted => { 
                if(isProposer)
                {
                    acceptedCounter++;
                    if(acceptedCounter == votes.Count - 1)
                    {
                        Console.WriteLine($"End {accepted.round} Round! Result: {accepted.result}");
                        CloseRound();
                    }
                }
            });
        }

        public void BroadcastTo(object message)
        {
            foreach (var peer in knownPeers)
                peer.Tell(message);
        }

        public string GetPeerName(IActorRef actorRef)
        {
            return $"{Regex.Match(actorRef.Path.ToString(), "@([\\w:0-9]+)/").Groups[1].Value}";
        }

        public void CloseRound()
        {
            votes.Clear();
            value = 0;
            acceptedCounter = 0;
        }
    }
}