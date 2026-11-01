using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Prometheus {
 public static class ProjectFileAssociation {
  const string ProgId="Prometheus.Project";
  const uint AssociationChanged=0x08000000;
  [DllImport("shell32.dll")]static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
  // Register the private file type, not a Windows default-app choice.
  public static void RegisterOrRefresh(){
   try{
    string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Prometheus.exe");
    string icon=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","PrometheusProject.ico");
    if(!File.Exists(executable)||!File.Exists(icon))return;
    string iconValue=icon+",0",command="\""+executable+"\" \"%1\"";
    bool changed=false;
    using(var type=Registry.CurrentUser.CreateSubKey(@"Software\Classes\.pthproj")){
     using(var defaultIcon=type.CreateSubKey("DefaultIcon")){
      if(!String.Equals(defaultIcon.GetValue("") as string,iconValue,StringComparison.OrdinalIgnoreCase)){defaultIcon.SetValue("",iconValue);changed=true;}
     }
     using(var openWith=type.CreateSubKey("OpenWithProgIds"))if(openWith.GetValue(ProgId)==null){openWith.SetValue(ProgId,"",RegistryValueKind.String);changed=true;}
    }
    using(var application=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ProgId)){
     application.SetValue("","Proyecto Prometheus");
     using(var defaultIcon=application.CreateSubKey("DefaultIcon"))if(!String.Equals(defaultIcon.GetValue("") as string,iconValue,StringComparison.OrdinalIgnoreCase)){defaultIcon.SetValue("",iconValue);changed=true;}
     using(var open=application.CreateSubKey(@"shell\open\command"))if(!String.Equals(open.GetValue("") as string,command,StringComparison.Ordinal)){open.SetValue("",command);changed=true;}
    }
    if(changed)SHChangeNotify(AssociationChanged,0,IntPtr.Zero,IntPtr.Zero);
   }catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}catch(IOException){}
  }
 }
}
