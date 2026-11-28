"""Non-destructive package checks. Does not open the main editor or user projects.
Usage: python Verify-Portable.py PORTABLE_DIRECTORY [--gpu] [--ai]
"""
import argparse,json,os,pathlib,subprocess,tempfile

def main():
    p=argparse.ArgumentParser();p.add_argument('directory',type=pathlib.Path);p.add_argument('--gpu',action='store_true');p.add_argument('--ai',action='store_true');args=p.parse_args()
    root=args.directory.resolve();env=dict(os.environ)
    for key in list(env):
        if key.startswith('PROMETHEUS_') or key.startswith('DOTNET_'):env.pop(key)
    env['DOTNET_ROOT']=str(root/'not-installed');env['DOTNET_MULTILEVEL_LOOKUP']='0'
    for path in ('Prometheus.Client.exe','hostfxr.dll','hostpolicy.dll','coreclr.dll','Runtime/ffmpeg/ffmpeg.exe','Runtime/ffmpeg/ffprobe.exe','yt-dlp.exe',
                 'McpBridge/Prometheus.McpBridge.exe','MgtLive/Prometheus.MgtLive.exe','MgtLive/libcef.dll','LICENSE','licenses/TERCEROS.md'):
        if not (root/path).is_file():raise RuntimeError('Missing '+path)
    requests=[{'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'portable-verifier','version':'1'}}},
              {'jsonrpc':'2.0','id':2,'method':'tools/list'}]
    result=subprocess.run([str(root/'McpBridge/Prometheus.McpBridge.exe')],input='\n'.join(json.dumps(x) for x in requests)+'\n',text=True,capture_output=True,timeout=20,env=env,cwd=root)
    if result.returncode:raise RuntimeError(result.stderr)
    replies=[json.loads(line) for line in result.stdout.splitlines()]
    dependencies=json.loads((root/'Prometheus.Client.deps.json').read_text(encoding='utf-8'))
    expected_version=next(key.split('/',1)[1] for key in dependencies['libraries'] if key.startswith('Prometheus.Client/'))
    assert replies[0]['result']['serverInfo']['version']==expected_version,replies
    assert len(replies[1]['result']['tools'])==11,replies
    print('PASS self-contained MCP initialization; 11 tools')
    for tool in ('ffmpeg','ffprobe'):
        result=subprocess.run([str(root/('Runtime/ffmpeg/'+tool+'.exe')),'-version'],capture_output=True,text=True,timeout=20,env=env,cwd=root)
        assert result.returncode==0,result.stderr
        assert tool+' version 9.1-prometheus-3.19.9' in result.stdout,result.stdout
    print('PASS multimedia tool startup')
    if args.gpu:
        result=subprocess.run([str(root/'MgtLive/Prometheus.MgtLive.exe'),'--gpu-probe'],capture_output=True,text=True,timeout=60,env=env,cwd=root)
        assert result.returncode==0,(result.stdout,result.stderr)
        assert 'gpu-ok' in result.stdout,result.stdout
        sample=root/'Ejemplos MGT/composition.mgt.json'
        result=subprocess.run([str(root/'MgtLive/Prometheus.MgtLive.exe'),'--client-probe',str(sample)],capture_output=True,text=True,timeout=120,env=env,cwd=root)
        assert result.returncode==0,(result.stdout,result.stderr)
        assert 'cross-process-gpu-ok' in result.stdout,result.stdout
        print('PASS CEF GPU texture, alpha, live parameters and cross-process sharing')
    if args.ai:
        python=root/'Runtime/whisper/python/python.exe'
        env['PATH']=str(python.parent/'Lib/site-packages/torch/lib')+os.pathsep+env.get('PATH','')
        script='import sys,torch,faster_whisper,ctranslate2;sys.path.insert(0,sys.argv[1]);import sam2;from faster_whisper import WhisperModel;m=WhisperModel(sys.argv[2],device="cpu",compute_type="int8",local_files_only=True);print("AI-OK cuda="+str(torch.cuda.is_available()))'
        result=subprocess.run([str(python),'-I','-B','-c',script,str(root/'Runtime/sam2/repo'),str(root/'Runtime/whisper/model')],capture_output=True,text=True,timeout=180,env=env,cwd=root)
        assert result.returncode==0,(result.stdout,result.stderr)
        assert 'AI-OK' in result.stdout,result.stdout
        assert (root/'Runtime/sam2/sam2.1_hiera_small.pt').is_file()
        print('PASS relocated Python imports, CUDA detection and offline Whisper model load')
    print('Verification completed. No main window or user project was opened.')
if __name__=='__main__':main()
