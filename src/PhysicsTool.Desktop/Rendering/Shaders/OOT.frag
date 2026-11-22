#version 330 core
// Adapted from Switch Toolbox's BFRES_Oot.frag (GPL-3.0).
in vec2 f_raw_texcoord0, f_raw_texcoord1, f_raw_texcoord2, f_raw_texcoord3;
in vec3 normal, objectPosition;
in vec4 vertexColor;
uniform mat4 ootView;
out vec4 fragColor;
uniform sampler2D ootTex0, ootTex1, ootTex2, ootTex3;
uniform sampler2D ootTex4, ootTex5, ootTex6, ootTex7;
uniform int ootPresent[8], ootSrgb[8], ootUv[8];
uniform vec4 ootSrt[8], ootChannels[8];
uniform float ootRotation[8];
uniform int ootGraph, ootAlbedoSelect;
uniform float ootScalar[8];
uniform vec4 ootParamColor[4], ootVector;
uniform vec4 ootColor;
uniform int ootAlpha, ootAlphaTest, ootAlphaFunc, ootOpaque, ootNormal, ootAo;
uniform float ootAlphaRef;
uniform int useNormalMap, renderDiffuse, renderSpecular;
uniform vec3 cameraPosition;
uniform int colorOverride;
uniform vec3 materialSelectColor;
float channel(vec4 v, int n) { return n==4 ? 0.0 : n==5 ? 1.0 : v[clamp(n,0,3)]; }
vec2 uv(int i) {
    vec2 p=ootUv[i]==1?f_raw_texcoord1:ootUv[i]==2?f_raw_texcoord2:ootUv[i]==3?f_raw_texcoord3:f_raw_texcoord0;
    float c=cos(ootRotation[i]),s=sin(ootRotation[i]);
    return vec2(c*p.x-s*p.y,s*p.x+c*p.y)*ootSrt[i].xy+ootSrt[i].zw;
}
vec3 linearColor(vec3 v) { return mix(v/12.92,pow((max(v,vec3(0))+.055)/1.055,vec3(2.4)),step(vec3(.04045),v)); }
vec3 displayColor(vec3 v) { return mix(v*12.92,1.055*pow(max(v,vec3(0)),vec3(1.0/2.4))-.055,step(vec3(.0031308),v)); }
vec4 sampleAt(int i,vec2 p,vec4 fallback) {
    if(ootPresent[i]==0)return fallback;
    vec4 v=i==0?texture(ootTex0,p):i==1?texture(ootTex1,p):i==2?texture(ootTex2,p):i==3?texture(ootTex3,p):
        i==4?texture(ootTex4,p):i==5?texture(ootTex5,p):i==6?texture(ootTex6,p):texture(ootTex7,p);
    v=vec4(channel(v,int(ootChannels[i].x)),channel(v,int(ootChannels[i].y)),channel(v,int(ootChannels[i].z)),channel(v,int(ootChannels[i].w)));
    if(ootSrgb[i]!=0)v.rgb=linearColor(v.rgb);
    return v;
}
vec4 sampleMap(int i,vec4 fallback) { return sampleAt(i,uv(i),fallback); }
mat3 surfaceBasis(vec3 n,vec2 p) {
    vec3 dp1=dFdx(objectPosition),dp2=dFdy(objectPosition);vec2 d1=dFdx(p),d2=dFdy(p);
    float det=d1.x*d2.y-d1.y*d2.x;
    vec3 t=abs(det)>1e-10?(dp1*d2.y-dp2*d1.y)/det:cross(abs(n.y)<.99?vec3(0,1,0):vec3(1,0,0),n);
    t=normalize(t-n*dot(t,n));
    return mat3(t,cross(n,t)*(det<0.0?-1.0:1.0),n);
}
vec3 unpackNormal(vec4 map) {
    // The compiled shaders decode signed BC5 from the 8-bit midpoint, not 0.5.
    vec2 xy=map.xy*(255.0/127.0)-(128.0/127.0);
    return vec3(xy,sqrt(max(0.0,1.0-dot(xy,xy))));
}
vec3 rgbToHsv(vec3 c) {
    vec4 k=vec4(0,-1.0/3.0,2.0/3.0,-1);
    vec4 p=mix(vec4(c.bg,k.wz),vec4(c.gb,k.xy),step(c.b,c.g));
    vec4 q=mix(vec4(p.xyw,c.r),vec4(c.r,p.yzx),step(p.x,c.r));
    float d=q.x-min(q.w,q.y);
    return vec3(abs(q.z+(q.w-q.y)/(6.0*d+1e-10)),d/(q.x+1e-10),q.x);
}
vec3 hsvToRgb(vec3 c) {
    vec3 p=abs(fract(c.xxx+vec3(0,2.0/3.0,1.0/3.0))*6.0-3.0);
    return c.z*mix(vec3(1),clamp(p-1.0,0.0,1.0),c.y);
}
bool alphaPass(float a) {
    if(ootAlphaTest==0)return true;
    if(ootAlphaFunc==0)return a>=ootAlphaRef;
    if(ootAlphaFunc==1)return a>ootAlphaRef;
    if(ootAlphaFunc==2)return a==ootAlphaRef;
    if(ootAlphaFunc==3)return a<ootAlphaRef;
    if(ootAlphaFunc==4)return a<=ootAlphaRef;
    if(ootAlphaFunc==5)return a!=ootAlphaRef;
    if(ootAlphaFunc==6)return false;
    return true;
}
void main() {
    if(colorOverride==1){fragColor=vec4(dot(materialSelectColor,materialSelectColor)>0.0?materialSelectColor:vec3(1),1);return;}
    vec4 base=sampleMap(0,vec4(.55,.55,.55,1))*ootColor;
    float alpha=ootAlpha!=0?base.a:1.0;
    vec3 n=normalize(normal)*(gl_FrontFacing?1.0:-1.0);
    vec3 v=normalize(cameraPosition-objectPosition);
    mat3 basis=surfaceBasis(n,uv(1));
    if(ootGraph==4){
        // Eyelid shadow stores coverage in red. Native RGB is scene-ambient lighting;
        // use the neutral preview ambient until the scene irradiance path is available.
        vec3 direction=length(ootVector.xyz)>1e-8?normalize(ootVector.xyz):vec3(0,1,0);
        float facing=clamp(dot(n,direction),0.0,1.0);
        alpha=sampleMap(0,vec4(0)).r*ootScalar[1]*mix(1.0,facing,clamp(ootScalar[0],0.0,1.0));
        fragColor=vec4(vec3(.08),clamp(alpha,0.0,1.0));return;
    }
    if(ootGraph==1){
        // Inner-eye surface graph: depth-refraction, HSV iris adjustment, then sclera/mask composition.
        vec2 p=f_raw_texcoord0;
        vec2 maskUv=(p-.5)*ootScalar[3]+.5;
        vec3 flattenNormal=normalize(basis*unpackNormal(sampleAt(6,p,vec4(128.0/255.0,128.0/255.0,1,1))));
        vec3 ray=refract(-v,flattenNormal,.730138719);
        vec2 rayUv=vec2(dot(ray,basis[0]),-dot(ray,basis[1]));
        float depth=pow(abs(sampleAt(4,maskUv,vec4(0)).r),2.2)*max(ootScalar[1],0.0);
        vec2 irisUv=(p-.5)*(ootScalar[3]+ootScalar[2])+.5+rayUv*depth*(.6-.3*(mat3(ootView)*flattenNormal).z);
        vec3 iris=sampleAt(0,irisUv,vec4(.55,.55,.55,1)).rgb;
        iris=hsvToRgb(clamp(rgbToHsv(iris)+2.0*ootParamColor[2].rgb-1.0,0.0,1.0));
        vec3 sclera=clamp(sampleAt(2,p,vec4(1)).rgb*(2.0*ootParamColor[2].a),0.0,1.0);
        float mask=clamp(sampleAt(5,maskUv,vec4(1)).r,0.0,1.0);
        base=vec4(mix(sclera,iris,mask),1);
        if(useNormalMap!=0)n=normalize(basis*unpackNormal(sampleAt(1,maskUv,vec4(128.0/255.0,128.0/255.0,1,1))));
        alpha=1.0;
    }
    else if(ootGraph==2){
        // The native cornea outputs zero alpha with ONE/ONE additive blending.
        vec2 p=f_raw_texcoord0;
        vec3 nt=unpackNormal(sampleAt(1,(p-.5)*ootScalar[4]+.5,vec4(128.0/255.0,128.0/255.0,1,1)));
        vec3 eyeN=normalize(basis*nt);
        vec3 vn=normalize(mat3(ootView)*eyeN);
        vec2 projected=vec2(vn.x,-vn.y)*.5+.5;
        vec2 specUv=(mix(p,projected,ootScalar[2])-.5)*ootScalar[0];
        float angle=ootScalar[3]*6.283185307,c=cos(angle),s=sin(angle);
        specUv=vec2(c*specUv.x+s*specUv.y,c*specUv.y-s*specUv.x)+.5;
        float fresnel=pow(1.0-clamp(dot(eyeN,v),0.0,1.0),5.0);
        float strength=max(0.0,mix(ootVector.x,1.0,pow(max(fresnel,1e-8),ootVector.y)));
        float highlight=sampleAt(0,specUv,vec4(0)).r*strength;
        vec3 l=normalize(vec3(.35,.8,.55));
        float gloss=renderSpecular!=0?.04*pow(max(dot(eyeN,normalize(l+v)),0.0),mix(256.0,8.0,clamp(ootScalar[1],0.0,1.0))):0.0;
        fragColor=vec4(displayColor(vec3(highlight+gloss)),0);return;
    }
    else if(ootGraph==3){
        // Hair operation0 multiplies albedo alpha by the second-UV cutout mask.
        alpha=base.a*sampleMap(5,vec4(1)).r;
    }
    else if(ootGraph==5&&(ootAlbedoSelect==204||ootAlbedoSelect==206)){
        // The native vertex/fragment pair passes _c0.r through as the root-to-tip tint.
        base.rgb=mix(ootParamColor[2].rgb,ootParamColor[3].rgb,vertexColor.r);
        if(ootAlbedoSelect==206)base.rgb*=mix(1.0,sampleMap(0,vec4(1)).r,ootScalar[5]);
    }
    if(!alphaPass(alpha))discard;
    if(ootGraph!=1&&ootNormal!=0&&useNormalMap!=0&&ootPresent[1]!=0){
        n=normalize(basis*unpackNormal(sampleMap(1,vec4(128.0/255.0,128.0/255.0,1,1))));
    }
    float ao=ootGraph!=1&&ootAo!=0?mix(.45,1.0,clamp(sampleMap(2,vec4(1)).r,0.0,1.0)):1.0;
    if(ootGraph==3&&ootAo!=0){
        // Hair graph shapes texture AO and vertex-red coverage together (native 0x1b00-0x1c90).
        float coverage=clamp(sampleMap(2,vec4(1)).r*clamp(vertexColor.r,0.0,1.0),0.0,1.0);
        ao=pow(coverage,max(clamp(1.0-ootScalar[2],0.0,1.0),.001));
    }
    vec3 l=normalize(vec3(.35,.8,.55));
    float light=renderDiffuse!=0?.38+.62*max(dot(n,l),0.0):1.0;
    vec3 rgb=base.rgb*light*ao;
    if(renderSpecular!=0){
        float rough=ootGraph==1?ootScalar[0]:sampleMap(3,vec4(.7)).r;
        if(ootGraph==3)rough+=clamp(ootScalar[3],-1.0,1.0);
        rough=clamp(rough,.15,1.0);
        vec3 tint=ootGraph==3?ootParamColor[1].rgb:vec3(1);
        rgb+=tint*(.025*pow(max(dot(n,normalize(l+v)),0.0),mix(100.0,4.0,rough)))*ao;
    }
    fragColor=vec4(displayColor(rgb),ootOpaque!=0?1.0:clamp(alpha,0.0,1.0));
}
