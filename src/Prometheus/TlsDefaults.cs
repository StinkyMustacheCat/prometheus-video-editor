using System;
using System.Net;

namespace Prometheus {
 internal static class TlsDefaults {
  // 0 is SystemDefault on current .NET Framework runtimes. Older runtimes
  // reject it, so use TLS 1.2 rather than falling back to legacy protocols.
  internal static void Apply(){
   try{ServicePointManager.SecurityProtocol=(SecurityProtocolType)0;}
   catch(NotSupportedException){ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;}
   catch(ArgumentException){ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;}
  }
 }
}
