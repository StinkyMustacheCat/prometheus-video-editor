"""Package a reviewed release. Standard-library Python 3.11+; no upload operation.
All binary ZIPs extract into the same versioned directory. Sources are separate.
"""
import argparse,datetime,hashlib,json,pathlib,zipfile,concurrent.futures

def sha(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def files(folder):return sorted((p for p in folder.rglob('*') if p.is_file() and p.suffix!='.pyc' and '__pycache__' not in p.relative_to(folder).parts),key=lambda p:p.as_posix())
def source_files(folder):
    excluded={'bin','obj','__pycache__','.git','Dependencies','TestResults'}
    return [p for p in files(folder) if not any(part in excluded for part in p.relative_to(folder).parts)]
def archive(path,entries):
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as z:
        for source,target in entries:z.write(source,target)
    # GitHub release assets must remain below 2 GB. Use a conservative limit.
    if path.stat().st_size>=1_950_000_000:raise RuntimeError('Archive exceeds release asset limit: '+str(path))
    print('PACKED',path.name,path.stat().st_size,flush=True)
def main():
    p=argparse.ArgumentParser();p.add_argument('staging',type=pathlib.Path);p.add_argument('--version',default='3.19.9');p.add_argument('--review-only',action='store_true');args=p.parse_args()
    root=args.staging.resolve();ready=json.loads((root/'release-readiness.json').read_text(encoding='utf-8'))
    if not ready.get('public_binary_release_ready') and not args.review_only:
        raise RuntimeError('Binary publication is pending. Read release-readiness.json; use --review-only for local review packages.')
    assets=root/'Release-assets';assets.mkdir(exist_ok=True)
    portable=root/'Portable'/('Prometheus-'+args.version)
    def fingerprint(p):return {'path':p.relative_to(portable).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)}
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        fingerprints=list(pool.map(fingerprint,[p for p in files(portable) if p.name!='FILES-SHA256.json']))
    manifest={'version':args.version,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'files':fingerprints}
    (portable/'FILES-SHA256.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    prefix='Prometheus-'+args.version+('-REVIEW' if args.review_only else '')
    base=[];ai=[]
    for f in files(portable):
        relative=f.relative_to(portable).as_posix()
        target=portable.name+'/'+relative
        (ai if relative.startswith(('Runtime/whisper/','Runtime/sam2/')) else base).append((f,target))
    archive(assets/(prefix+'-Portable.zip'),base)
    # Bound uncompressed totals so even incompressible input stays below the asset limit.
    groups=[];group=[];size=0
    for entry in ai:
        n=entry[0].stat().st_size
        if n>1_700_000_000:raise RuntimeError('Single AI file is too large: '+str(entry[0]))
        if group and size+n>1_700_000_000:groups.append(group);group=[];size=0
        group.append(entry);size+=n
    if group:groups.append(group)
    for i,group in enumerate(groups,1):archive(assets/(prefix+f'-AI-{i:02d}.zip'),group)
    for folder,label in [('GitHub','Source'),('Dependencies','Dependencies'),('ThirdPartySources','ThirdPartySources')]:
        directory=root/folder
        entries=source_files(directory) if folder=='GitHub' else files(directory)
        mapped=[(f,('Prometheus-'+args.version+'-Source/'+f.relative_to(directory).as_posix()) if folder=='GitHub' else ('Prometheus-'+args.version+'-Source/Dependencies/'+f.relative_to(directory).as_posix()) if folder=='Dependencies' else (folder+'/'+f.relative_to(directory).as_posix())) for f in entries]
        if folder == 'ThirdPartySources':
            chunks=[];chunk=[];total=0
            for item in mapped:
                size=item[0].stat().st_size
                if size>1_700_000_000:raise RuntimeError('Single source file exceeds asset limit: '+str(item[0]))
                if chunk and total+size>1_700_000_000:chunks.append(chunk);chunk=[];total=0
                chunk.append(item);total+=size
            if chunk:chunks.append(chunk)
            for index,chunk in enumerate(chunks,1):archive(assets/(prefix+f'-{label}-{index:02d}.zip'),chunk)
        else:archive(assets/(prefix+'-'+label+'.zip'),mapped)
    all_assets=sorted(assets.glob(prefix+'-*.zip'))
    (assets/'SHA256SUMS.txt').write_text('\n'.join(sha(p)+'  '+p.name for p in all_assets)+'\n',encoding='ascii')
    (assets/'release-readiness.json').write_text(json.dumps(ready,ensure_ascii=False,indent=2),encoding='utf-8')
    print('COMPLETE',len(all_assets),'archives',flush=True)
if __name__=='__main__':main()
