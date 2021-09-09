using Akka;
using Akka.Actor;
using Akka.Configuration;
using PaxosCSharp;
using static PaxosCSharp.IPaxosService;

int localPort = 8001;
while (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
    .GetActiveTcpListeners().Any(l => l.Port == localPort))
    localPort++;

var config = ConfigurationFactory.ParseString(@"
akka {
    loglevel = ERROR
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
    } remote {
        helios.tcp {
            port = " + localPort +  @"
            hostname = localhost
        }
    }
}
");
Console.Title = $"localhost:{localPort}";

var actorSystem = ActorSystem.Create($"PaxosSystem", config);
var paxosService = actorSystem.ActorOf(Props.Create<PaxosService>(actorSystem), "PaxosService");

string line = string.Empty;
while (line != null)
{
    line = Console.ReadLine();
    if (line == "")
        continue;

    if (line[0] != '/') {
        paxosService.Tell(line);
        continue;
    }
        
    var splitLine = line.Split(' ');
    switch (splitLine[0].ToLower())
    {
        case "/cheat":
            paxosService.Tell(new CheatProposer());
            break;
        case "/round":
            paxosService.Tell(new GetRound());
            break;

        case "/prepare":
            paxosService.Tell(new Prepare(int.Parse(splitLine[1]), int.Parse(splitLine[2])));
            break;
        case "/accept":
            paxosService.Tell(new Accept(int.Parse(splitLine[1])));
            break;
    }
    
}

Console.Read();
actorSystem.Dispose();