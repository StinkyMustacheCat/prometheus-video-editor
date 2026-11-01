using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace Prometheus {
 public sealed partial class MediaPanelView {
  readonly Dictionary<string,Bitmap> thumbnails=new Dictionary<string,Bitmap>();
  readonly Dictionary<string,string> thumbnailKeys=new Dictionary<string,string>();
  readonly HashSet<string> loadingThumbnails=new HashSet<string>();
  void QueueVisibleThumbnails(){
   if(viewMode==2)return;
   if(!library.IsHandleCreated)return;
   int first=Math.Max(0,library.TopIndex),last=Math.Min(rows.Count,first+Math.Max(12,library.ClientSize.Height/Math.Max(1,library.ItemHeight)+3));
   for(int i=first;i<last;i++)if(rows[i].asset!=null){var asset=rows[i].asset;string key=MediaThumbnail.Key(asset);string saved;if(!thumbnailKeys.TryGetValue(asset.id,out saved)||saved!=key)QueueThumbnail(asset,key);}
  }
  void QueueThumbnail(Asset asset,string key){
   string id=asset.id;if(loadingThumbnails.Contains(id))return;loadingThumbnails.Add(id);
   Task.Factory.StartNew(()=>{
    Bitmap bitmap=null;try{bitmap=MediaThumbnail.Create(asset,key);}catch{}
    try{if(IsDisposed||!library.IsHandleCreated){if(bitmap!=null)bitmap.Dispose();return;}
     BeginInvoke((Action)(()=>{
      loadingThumbnails.Remove(id);
      if(IsDisposed){if(bitmap!=null)bitmap.Dispose();return;}
      if(!assets.Any(a=>a.id==id)||MediaThumbnail.Key(assets.First(a=>a.id==id))!=key){if(bitmap!=null)bitmap.Dispose();library.Invalidate();return;}
      Bitmap previous;if(thumbnails.TryGetValue(id,out previous))previous.Dispose();
      if(bitmap!=null)thumbnails[id]=bitmap;else thumbnails.Remove(id);
      thumbnailKeys[id]=key;library.Invalidate();
     }));
    }catch{if(bitmap!=null)bitmap.Dispose();}
   });
  }
  void DrawThumbnail(Graphics graphics,Asset asset,Rectangle bounds){
   using(var background=new SolidBrush(Color.FromArgb(49,53,59)))graphics.FillRectangle(background,bounds);
   if(asset==null)return;
   string key;try{key=MediaThumbnail.Key(asset);}catch{return;}
   Bitmap bitmap;string saved;
   if(thumbnails.TryGetValue(asset.id,out bitmap)&&thumbnailKeys.TryGetValue(asset.id,out saved)&&saved==key){graphics.DrawImage(bitmap,bounds);return;}
   if(library.IsHandleCreated&&(!thumbnailKeys.TryGetValue(asset.id,out saved)||saved!=key))QueueThumbnail(asset,key);
   using(var ink=new SolidBrush(Color.FromArgb(180,188,198)))using(var font=new Font("Segoe UI",8,FontStyle.Bold))graphics.DrawString(asset.streams.All(s=>s.kind=="audio")?"♪":asset.streams.Any(s=>s.kind=="image")?"▣":"▶",font,ink,bounds,new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});
  }
  public void RefreshThumbnail(string id){
   Bitmap bitmap;if(thumbnails.TryGetValue(id,out bitmap)){bitmap.Dispose();thumbnails.Remove(id);}thumbnailKeys.Remove(id);library.Invalidate();
  }
  void PruneThumbnails(){
   var valid=new HashSet<string>(assets.Select(a=>a.id));foreach(string id in thumbnails.Keys.Where(id=>!valid.Contains(id)).ToList())RefreshThumbnail(id);
  }
  void DisposeThumbnails(){foreach(var bitmap in thumbnails.Values)bitmap.Dispose();thumbnails.Clear();thumbnailKeys.Clear();}
 }
}
