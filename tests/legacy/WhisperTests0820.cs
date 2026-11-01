using System;
using System.Linq;
using System.Threading;
using Prometheus;
class WhisperTests0820 {static void Main(string[] args){Tools.Root=args[0];var result=Whisper0820.Transcribe(args[1],0,3.8,1,"en",CancellationToken.None);if(result.cues.Count==0||result.cues.Sum(c=>c.words.Count)<5)throw new Exception("Whisper no devolvió palabras con tiempos");Console.WriteLine("OK: Whisper integrado, "+result.cues.Sum(c=>c.words.Count)+" palabras");}}
