"""Read raw city FBX geometry without changing the Unity reference project.

Run from any directory, then run game/tools/repair_level3_meshes.gd.
The temporary JSON is an offline conversion input, not a runtime dependency.
"""
from pathlib import Path
import struct,zlib
root=Path(__file__).resolve().parents[1]
p=root/'game/assets/models/mobile_pp/Example/Objects/city.fbx'
f=open(p,'rb');hdr=f.read(27);version=struct.unpack('<I',hdr[23:27])[0];print(version,flush=True)
wide=version>=7500
fmt='<QQQB' if wide else '<IIIB';sz=25 if wide else 13

def prop():
 t=f.read(1).decode()
 fm={'Y':'h','C':'?','I':'i','F':'f','D':'d','L':'q'}
 if t in fm:return struct.unpack('<'+fm[t],f.read(struct.calcsize(fm[t])))[0]
 if t in 'SR':return f.read(struct.unpack('<I',f.read(4))[0])
 if t in 'fdilbc':
  n,e,l=struct.unpack('<III',f.read(12));b=f.read(l);b=zlib.decompress(b) if e else b
  ff={'f':'f','d':'d','i':'i','l':'q','b':'?','c':'b'}[t]
  return struct.unpack('<'+str(n)+ff,b)
 raise Exception(t)
def node():
 start=f.tell();h=f.read(sz)
 if len(h)<sz:return None
 end,count,plen,nlen=struct.unpack(fmt,h)
 if not end:return None
 name=f.read(nlen).decode();ps=[prop() for _ in range(count)];kids=[]
 while f.tell()<end:
  n=node()
  if n is None:break
  kids.append(n)
 f.seek(end)
 return name,ps,kids
nodes=[]
while True:
 n=node()
 if not n:break
 nodes.append(n)
obj=next(n for n in nodes if n[0]=='Objects')
# Resolve unnamed Geometry objects through their owning FBX Model connections.
objects={p[0]:(t,p,k) for t,p,k in obj[2] if p}
connections=next(n for n in nodes if n[0]=='Connections')[2]
meshes={}
for _, props, _ in connections:
    if len(props)<3 or props[1] not in objects or props[2] not in objects: continue
    geom,model=objects[props[1]],objects[props[2]]
    if geom[0]!='Geometry' or model[0]!='Model': continue
    gid=str(geom[1][0])
    if gid not in meshes:
        fields={a:b[0] for a,b,c in geom[2] if b and a in ('Vertices','PolygonVertexIndex')}
        layers={a:{aa:bb[0] for aa,bb,cc in c if bb} for a,b,c in geom[2] if a.startswith('LayerElement')}
        def clean(value):
            if isinstance(value,bytes): return value.decode()
            if isinstance(value,dict): return {k:clean(v) for k,v in value.items()}
            return value
        meshes[gid]={'names':[], 'vertices':fields['Vertices'], 'polygons':fields['PolygonVertexIndex'], 'layers':clean(layers)}
    meshes[gid]['names'].append(model[1][1].split(b'\x00')[0].decode())
import json
(root/'temp/city_geometry.json').write_text(json.dumps(list(meshes.values()),separators=(',',':')),encoding='utf-8')
print('Exported raw FBX geometries:',len(meshes))
