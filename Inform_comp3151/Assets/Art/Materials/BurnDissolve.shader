// Inkform burn-dissolve — port of "Pixelated burn" (godotshaders.com/shader/pixelated-burn/,
// CC0 by Demyr) to URP 2D sprites. A circle of fire expands from the ignition point: a WIDE,
// posterized band of fire colors rides the edge (so the burn is visible while it eats the
// body), everything inside the radius is burned through to transparency.
//
// The original, verbatim:
//     vec2 snapped_uv = floor(UV / pixel_size) * pixel_size;
//     float dist = length(position - snapped_uv) + texture(noiseTexture, snapped_uv).b * burnMult;
//     float mask = clamp((dist - (radius - borderWidth)) / (2.0 * borderWidth), 0.0, 1.0);
//     mask = 1.0 - mask;
//     mask = floor(mask * blend_steps) / blend_steps;
//     vec4 curve_value = texture(colorCurve, vec2(mask, 0.0));
//     COLOR.rgb = mix(COLOR.rgb, curve_value.rgb, pow(mask, 0.1));
//     COLOR.a *= min(COLOR.a, 1.0 - float(dist < radius));
//
// Port notes:
//   • position/radius are our _BurnOrigin/_BurnFront, driven per-frame by BurnAwayFx;
//   • pixel_size becomes _Chunk (texel-driven); the noise texture becomes in-shader value
//     noise (same organic wobble, no asset to wire);
//   • the colorCurve gradient texture becomes an in-shader 4-stop fire ramp — the hand-drawn
//     palette look with no texture asset;
//   • distance lives in "aspect space" (u pre-multiplied by width/height) so the circle is
//     round in world units on non-square sprites.
Shader "Inkform/BurnDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BurnOrigin ("Burn Origin (aspect-space UV)", Vector) = (0.5, 0.5, 0, 0)
        _Aspect ("Aspect (width / height)", Float) = 1
        _BurnFront ("Burn Radius", Float) = -1

        _BorderWidth ("Burn Border Width", Range(0.01, 1)) = 0.2
        _BurnMult ("Noise Burn Mult", Range(0, 0.5)) = 0.34
        _NoiseScale ("Noise Scale", Range(1, 64)) = 6
        _Chunk ("Pixel Chunk", Range(1, 8)) = 2
        _BlendSteps ("Fire Band Steps", Range(2, 16)) = 8
        _EdgeBias ("Edge Color Bias", Range(0.05, 2)) = 0.1

        _RampChar ("Ramp: Char", Color) = (0.1, 0.05, 0.03, 1)
        _RampRed ("Ramp: Deep Red", Color) = (0.55, 0.1, 0.05, 1)
        _RampOrange ("Ramp: Orange", Color) = (0.95, 0.45, 0.08, 1)
        _RampHot ("Ramp: Hot", Color) = (1, 0.9, 0.55, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;   // zw = texture width/height, for pixel snapping
                float4 _Color;
                float4 _BurnOrigin;
                float  _Aspect;
                float  _BurnFront;
                float  _BorderWidth;
                float  _BurnMult;
                float  _NoiseScale;
                float  _Chunk;
                float  _BlendSteps;
                float  _EdgeBias;
                half4  _RampChar;
                half4  _RampRed;
                half4  _RampOrange;
                half4  _RampHot;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;      // SpriteRenderer.color rides in here
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            // Smooth value noise — stands in for the reference's Perlin NoiseTexture2D
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1.0, 0.0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // The colorCurve gradient texture, rebuilt in-shader: 0 = cold char → 1 = white-hot
            half3 FireRamp(float t)
            {
                return t < 1.0 / 3.0
                    ? lerp(_RampChar.rgb, _RampRed.rgb, t * 3.0)
                    : t < 2.0 / 3.0
                        ? lerp(_RampRed.rgb, _RampOrange.rgb, t * 3.0 - 1.0)
                        : lerp(_RampOrange.rgb, _RampHot.rgb, t * 3.0 - 2.0);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color * _Color;

                // snapped uv: every pixel of a chunk shares one distance and one noise value —
                // the whole effect moves in blocky jumps
                float2 px       = floor(IN.uv * _MainTex_TexelSize.zw);
                float2 chunk    = floor(px / max(1.0, _Chunk));
                float2 snappedUV = (chunk * _Chunk + 0.5 * _Chunk) * _MainTex_TexelSize.xy;

                float dist = distance(float2(snappedUV.x * _Aspect, snappedUV.y), _BurnOrigin.xy)
                           + ValueNoise(snappedUV * _NoiseScale) * _BurnMult;

                // the fire band: 1 deep inside (about to vanish) → 0 outside (unburned),
                // posterized into hard color steps
                float mask = clamp((dist - (_BurnFront - _BorderWidth)) / (2.0 * _BorderWidth), 0.0, 1.0);
                mask = 1.0 - mask;
                mask = floor(mask * _BlendSteps) / _BlendSteps;

                // edge bias: the fire color claims the body quickly as the front arrives
                sprite.rgb = lerp(sprite.rgb, FireRamp(mask), pow(mask, _EdgeBias));

                // inside the radius: burned through
                sprite.a *= 1.0 - step(dist, _BurnFront);
                return sprite;
            }
            ENDHLSL
        }
    }
}
