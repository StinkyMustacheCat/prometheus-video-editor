"""Inventory resolved NuGet packages and an optional bundled Python, preserving vendor notices.
Does not certify redistribution rights or complete corresponding source automatically.
Python 3.11+, standard library only.
"""
import argparse, email, hashlib, json, pathlib, re, shutil, xml.etree.ElementTree as ET

def sha(path):
    with path.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()
def notices(directory):
    for p in directory.rglob('*'):
        if p.is_file() and any(w in p.name.lower() for w in ('license','licence','copying','copyright','notice','authors')):
            if p.stat().st_size < 5_000_000 and p.suffix.lower() not in ('.dll','.pyc','.pyd','.png'): yield p
def copy_notices(base, destination):
    found=[]
    for f in notices(base):
        rel=f.relative_to(base); out=destination/rel
        out.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(f,out)
        found.append({'file':str(out),'sha256':sha(out)})
    return found
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--repo',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[1]);ap.add_argument('--python',type=pathlib.Path)
    args=ap.parse_args();root=args.repo;out=root/'licenses';out.mkdir(exist_ok=True)
    records={}
    for assets in (root/'src').glob('*/obj/project.assets.json'):
        data=json.loads(assets.read_text(encoding='utf-8-sig'))
        for key,lib in data['libraries'].items():
            if lib.get('type')!='package': continue
            if key in records:
                records[key]['used_by'].append(assets.parts[-3]);continue
            folder=next((pathlib.Path(p)/lib['path'] for p in data['packageFolders'] if (pathlib.Path(p)/lib['path']).is_dir()),None)
            if folder is None: raise RuntimeError('Missing restored package '+key)
            nuspec=next(folder.glob('*.nuspec'));tree=ET.parse(nuspec)
            ns={'n':'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
            def node(name): return next((e for e in tree.iter() if e.tag.split('}')[-1]==name),None)
            def value(name):
                e=node(name);return e.text if e is not None else None
            repo=node('repository');lic=node('license')
            dest=out/'nuget'/key.replace('/','-');dest.mkdir(parents=True,exist_ok=True)
            shutil.copy2(nuspec,dest/nuspec.name)
            found=copy_notices(folder,dest)
            records[key]={'ecosystem':'NuGet','id':key.split('/')[0],'version':key.split('/')[1],
              'license':value('license'),'license_type':lic.get('type') if lic is not None else None,
              'license_url':value('licenseUrl'),'project_url':value('projectUrl'),'copyright':value('copyright'),
              'repository':dict(repo.attrib) if repo is not None else None,'used_by':[assets.parts[-3]],'notices':found,
              'package_sha512':lib.get('sha512')}
    python_records=[]
    if args.python:
        site=args.python/'Lib'/'site-packages'
        for info in sorted(site.glob('*.dist-info')):
            meta=email.message_from_bytes((info/'METADATA').read_bytes())
            dest=out/'python'/info.name;dest.mkdir(parents=True,exist_ok=True)
            for filename in ('METADATA','WHEEL'):
                if (info/filename).exists():shutil.copy2(info/filename,dest/filename)
            found=copy_notices(info,dest)
            for folder in (site/(meta['Name'] or '').replace('-','_'),):
                if folder.is_dir():found+=copy_notices(folder,dest/'package')
            python_records.append({'ecosystem':'PyPI','id':meta['Name'],'version':meta['Version'],
               'license':meta['License-Expression'] or meta['License'],'classifiers':meta.get_all('Classifier',[]),
               'project_urls':meta.get_all('Project-URL',[]),'homepage':meta['Home-page'],'notices':found})
        copy_notices(args.python/'DLLs',out/'python'/'runtime-dlls')
        for p in args.python.glob('*'):
            if p.is_file() and 'license' in p.name.lower(): shutil.copy2(p,out/'python'/p.name)
    all_records=list(records.values())+python_records
    # Public inventory contains relative notice paths, never the operator's local directories.
    for record in all_records:
        for n in record['notices']:n['file']=pathlib.Path(n['file']).relative_to(root).as_posix()
    (out/'components.json').write_text(json.dumps({'format':1,'release':'3.19.9','components':all_records},ensure_ascii=False,indent=2,default=str),encoding='utf-8')
    print(json.dumps({'nuget':len(records),'python':len(python_records),'no_notice':[r['id'] for r in all_records if not r['notices']]}))
if __name__=='__main__':main()
