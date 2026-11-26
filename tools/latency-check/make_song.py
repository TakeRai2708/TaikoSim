# 音の遅れを測る専用の曲を作る（120BPM・0.5秒ごとに 2kHz の「ピッ」・最初は 2.000秒）
#   使い方：python make_song.py → 測定用/ フォルダができる。TakeTaiko の Songs に入れる（ogg にするには ffmpeg が要る）
import numpy as np, wave, os, subprocess
sr=44100; bpm=120; beat=60/bpm; lead=2.0; beats=80
a=np.zeros(int(sr*(lead+beats*beat+2)),dtype=np.float32)
t=np.arange(int(sr*0.012))/sr
click=(np.sin(2*np.pi*2000*t)*np.exp(-t/0.004)).astype(np.float32)*0.8
for i in range(beats):
    s=int(round((lead+i*beat)*sr)); a[s:s+len(click)]+=click
os.makedirs('測定用/遅れ測定',exist_ok=True)
pcm=(np.clip(a,-1,1)*32767).astype(np.int16)
with wave.open('click.wav','wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(sr); w.writeframes(np.repeat(pcm,2).tobytes())
subprocess.run(['ffmpeg','-v','error','-y','-i','click.wav','-c:a','libvorbis','-q:a','8','測定用/遅れ測定/click.ogg'],check=True)
os.remove('click.wav')
tja="TITLE:遅れ測定（120BPM・ピッ）\nSUBTITLE:TakeTaiko 音の遅れを測る用\nBPM:120\nWAVE:click.ogg\nOFFSET:-2.000\nDEMOSTART:2.000\n\nCOURSE:Oni\nLEVEL:1\nBALLOON:\nSCOREINIT:\nSCOREDIFF:\n\n#START\n"+"1111,\n"*(beats//4)+"#END\n"
open('測定用/遅れ測定/遅れ測定.tja','w',encoding='utf-8-sig').write(tja)
open('測定用/box.def','w',encoding='utf-8-sig').write("#TITLE:測定用\n#GENRE:バラエティ\n")
