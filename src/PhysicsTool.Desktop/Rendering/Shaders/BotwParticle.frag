#version 330 core
in vec2 vAlbedoUv;
uniform sampler2D uMask;
uniform sampler2D uNoise;
uniform vec4 uParticleColor0;
uniform vec4 uParticleColor1;
uniform vec4 uTexture1Srt;
uniform float uParticleAlpha;
out vec4 FragColor;
void main()
{
    vec4 mask = texture(uMask, clamp(vAlbedoUv, vec2(0), vec2(1)));
    float noise = texture(uNoise, vAlbedoUv * uTexture1Srt.xy + uTexture1Srt.zw).a;
    // Native resident variation 495: two-color interpolation and alpha dissolve.
    vec3 color = mix(uParticleColor1.rgb, uParticleColor0.rgb, mask.rgb);
    float alpha = clamp((mask.a * noise - uParticleColor0.a) * uParticleColor1.a, 0.0, 1.0) * uParticleAlpha;
    if (alpha <= 0.0001) discard;
    color = max(color, vec3(0));
    // The actor preview has an SDR target, not the game's HDR post stack.
    // Compress the peak together so HDR energy does not clip blue into white.
    color /= 1.0 + max(color.r, max(color.g, color.b));
    color = mix(12.92 * color, 1.055 * pow(color, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), color));
    FragColor = vec4(color, alpha);
}
