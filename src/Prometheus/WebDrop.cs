using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class WebDropInfo {public string Url,PageUrl;public bool IsYouTube;}
 public static class WebDrop {
  static string TextOf(object value){
   if(value is string)return (string)value;
   var stream=value as MemoryStream;if(stream==null)return null;
   byte[] bytes=stream.ToArray();if(bytes.Length==0)return null;
   return (bytes.Length>1&&bytes[1]==0?Encoding.Unicode:Encoding.UTF8).GetString(bytes).TrimEnd('\0');
  }
  static string Url(string value,string baseUrl){
   if(String.IsNullOrWhiteSpace(value))return null;
   value=WebUtility.HtmlDecode(value.Trim().Trim('"','\''));
   Uri parsed;if(Uri.TryCreate(value,UriKind.Absolute,out parsed))return parsed.Scheme=="http"||parsed.Scheme=="https"?parsed.AbsoluteUri:null;
   Uri page;if(Uri.TryCreate(baseUrl,UriKind.Absolute,out page)&&Uri.TryCreate(page,value,out parsed))return parsed.Scheme=="http"||parsed.Scheme=="https"?parsed.AbsoluteUri:null;
   return null;
  }
  static string HtmlImage(string html,string page){
   if(String.IsNullOrWhiteSpace(html))return null;
   var tag=Regex.Match(html,"<img\\b[^>]*>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
   if(!tag.Success)return null;
   foreach(string attr in new[]{"data-src","src"}){
    var match=Regex.Match(tag.Value,"\\b"+attr+"\\s*=\\s*(?:[\"'](?<url>[^\"']+)[\"']|(?<url>[^\\s>]+))",RegexOptions.IgnoreCase);
    if(match.Success){string found=Url(match.Groups["url"].Value,page);if(found!=null)return found;}
   }
   return null;
  }
  public static WebDropInfo Read(IDataObject data){
   if(data==null)return null;
   string html=data.GetDataPresent(DataFormats.Html)?TextOf(data.GetData(DataFormats.Html)):null;
   string page=null;if(!String.IsNullOrEmpty(html)){var match=Regex.Match(html,@"(?im)^SourceURL:(.+)$");if(match.Success)page=Url(match.Groups[1].Value,null);}
   string image=HtmlImage(html,page);if(image!=null)return new WebDropInfo{Url=image,PageUrl=page??image};
   foreach(string format in new[]{DataFormats.UnicodeText,DataFormats.Text,"UniformResourceLocatorW","UniformResourceLocator"}){
    if(!data.GetDataPresent(format))continue;string raw=TextOf(data.GetData(format));if(String.IsNullOrWhiteSpace(raw))continue;
    string link=Url(raw.Split('\n')[0].Trim(),null);if(link==null)continue;
    if(WebMedia.IsVideoUrl(link))return new WebDropInfo{Url=link,PageUrl=page??link,IsYouTube=WebMedia.IsYouTubeUrl(link)};
    if(WebMedia.IsImageUrl(link))return new WebDropInfo{Url=link,PageUrl=page??link};
   }
   return null;
  }
 }
}
