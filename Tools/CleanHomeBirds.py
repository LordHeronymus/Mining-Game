import json
import urllib.request
from pathlib import Path
import numpy as np
import soundfile as sf
from scipy import signal, ndimage

root=Path('Design/Tiefenhall/HomeAmbience/L1/Birds')
root.mkdir(parents=True,exist_ok=True)
reports=[]
for i,item in enumerate(json.loads(Path('Temp/bird-download.json').read_text()),1):
    original=root/f'Birds-{i}-original.mp3'
    if not original.exists(): urllib.request.urlretrieve(item['url'],original)
    x,sr=sf.read(original,always_2d=True)
    # Stereo-linked spectral reduction estimated from quiet periods.
    f,t,z=signal.stft(x.T,fs=sr,nperseg=2048,noverlap=1536,boundary='zeros')
    power=np.mean(np.abs(z)**2,axis=0)
    energy=power.sum(axis=0)
    quiet=energy<=np.quantile(energy,.25)
    floor=np.maximum(np.quantile(power[:,quiet],.65,axis=1),
                     np.quantile(power,.55,axis=1))[:,None]+1e-18
    ratio=power/floor
    mask=np.clip((ratio-4)/(ratio+1),0,1)**1.5
    mask=ndimage.gaussian_filter(mask,sigma=(1.0,1.5))
    mask=np.maximum(mask,.003)
    # Remove low humming/rumble; preserve bird call frequencies.
    mask*=np.clip((f[:,None]-180)/250,0,1)
    _,y=signal.istft(z*mask[None,:,:],fs=sr,nperseg=2048,noverlap=1536,boundary=True)
    y=y.T[:len(x)]
    fade=min(int(sr*.03),len(y)//4)
    ramp=np.sin(np.linspace(0,np.pi/2,fade))**2
    y[:fade]*=ramp[:,None];y[-fade:]*=ramp[::-1,None]
    assert np.isfinite(y).all() and np.max(np.abs(y))<1
    sf.write(root/f'Birds-{i}-clean.wav',y,sr,subtype='PCM_24')
    after=np.mean(np.abs(z*mask[None,:,:])**2,axis=0)
    loud=energy>=np.quantile(energy,.9)
    report=dict(variant=i,generationId=item['id'],seconds=len(y)/sr,
        quietReductionDb=float(10*np.log10(after[:,quiet].sum()/power[:,quiet].sum())),
        loudChangeDb=float(10*np.log10(after[:,loud].sum()/power[:,loud].sum())),
        peak=float(np.max(np.abs(y))))
    reports.append(report)
(root/'Cleanup-report.json').write_text(json.dumps(reports,indent=2))
print(json.dumps(reports,indent=2))
