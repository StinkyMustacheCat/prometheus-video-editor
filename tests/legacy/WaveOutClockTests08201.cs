using System;
using System.Threading;
using Prometheus;

class WaveOutClockTests08201 {
 static void Main(string[] args){Tools.Root=args[0];string command="-hide_banner -loglevel error -i "+Tools.Q(args[1])+" -f s16le -ar 48000 -ac 2 pipe:1";using(var output=new WaveOutPcm(command,CancellationToken.None)){output.Start();Thread.Sleep(150);var first=output.PlayedSeconds;Thread.Sleep(150);var second=output.PlayedSeconds;Console.WriteLine("clock "+first+" -> "+second);if(!first.HasValue||!second.HasValue||second.Value<=first.Value)throw new Exception("El reloj del dispositivo de audio no avanzó");Console.WriteLine("OK reloj de audio: "+first.Value.ToString("F3")+" → "+second.Value.ToString("F3")+" s");}}
}
