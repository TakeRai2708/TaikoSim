import subprocess, numpy as np, sys
f=sys.argv[1]; W,H=1920,1080
# video: only lane band y 370..410, x 640..1600
x0,x1,y0,y1=640,1600,372,406
cmd=['ffmpeg','-v','error','-i',f,'-vf',f'crop={x1-x0}:{y1-y0}:{x0}:{y0}','-f','rawvideo','-pix_fmt','rgb24','-']
p=subprocess.run(cmd,capture_output=True).stdout
fw,fh=x1-x0,y1-y0; n=len(p)//(fw*fh*3)
fr=np.frombuffer(p[:n*fw*fh*3],np.uint8).reshape(n,fh,fw,3).astype(int)
red=(fr[...,0]>200)&(fr[...,1]<120)&(fr[...,2]<90)
est=[]  # (frame, x)
from scipy import ndimage
for i in range(n):
    col=red[i].sum(axis=0)
    lab,k=ndimage.label(col>3)
    for j in range(1,k+1):
        xs=np.where(lab==j)[0]
        if len(xs)<40: continue  # note face ~ 90px wide
        w=col[xs]; cx=(xs*w).sum()/w.sum()+x0
        if xs.min()==0 or xs.max()==fw-1: continue
        est.append((i,cx))
est=np.array(est)
fps=60.0
t=est[:,0]/fps; x=est[:,1]
# speed: fit using consecutive frames per note: estimate v via dx/dt of nearest blobs
# arrival estimates assuming v; first get v by linear fit of (t, x) within clusters
# cluster by arrival guess with v=720
v=720.0
for it in range(3):
    arr=t+(x-620)/v
    order=np.argsort(arr); arr_s=arr[order]
    groups=np.split(order,np.where(np.diff(arr_s)>0.1)[0]+1)
    vs=[]
    for g in groups:
        if len(g)>5:
            A=np.polyfit(t[g],x[g],1); vs.append(-A[0])
    v=np.median(vs)
arr=t+(x-620)/v
order=np.argsort(arr); arr_s=arr[order]
groups=np.split(order,np.where(np.diff(arr_s)>0.1)[0]+1)
vis=np.array([np.median(arr[g]) for g in groups if len(g)>5])
print('speed px/s',round(v,1),'notes',len(vis))
# audio
a=subprocess.run(['ffmpeg','-v','error','-i',f,'-ac','1','-ar','48000','-f','f32le','-'],capture_output=True).stdout
a=np.frombuffer(a,np.float32)
from scipy.signal import butter,sosfilt
sos=butter(4,[1500,2600],btype='band',fs=48000,output='sos'); b=np.abs(sosfilt(sos,a))
env=np.convolve(b,np.ones(48)/48,'same')
thr=env.max()*0.25
on=[]; i=0
idx=np.where(env>thr)[0]
last=-1e9
for k in idx:
    if k-last>48000*0.2:
        # refine: first sample where env exceeds 10% of local max in next 5ms
        seg=env[k-240:k+480]; m=seg.max(); kk=k-240+np.argmax(seg>m*0.15)
        on.append(kk/48000)
    last=k
on=np.array(on)
print('audio onsets',len(on))
lags=[]
for tv in vis:
    j=np.argmin(np.abs(on-tv))
    if abs(on[j]-tv)<0.2: lags.append((on[j]-tv)*1000)
lags=np.array(lags)
good=lags[np.abs(lags-np.median(lags))<20]
print("pairs",len(lags),"good",len(good),"mean %.1f median %.1f sd %.1f p10 %.1f p90 %.1f"%(good.mean(),np.median(good),good.std(),np.percentile(good,10),np.percentile(good,90)))
