// Inkform sprite-outline — URP 2D sprite edge glow for interaction prompts. A second SpriteRenderer
// above the original draws with this material: it discards the body and paints only a band hugging
// the alpha silhouette (8 directions × 2 sample rings in texel space), so the light reads as a rim
// around the sprite whatever its shape — no scaled-sprite hacks, no per-art outline assets.
//
// Driven by InteractionPromptPart:
//   • _Fade (0..1) is the fade-in/out of the whole glow (plus the breathing pulse), set per frame;
//   • _OutlineWidth is in texels, so one setting looks equally thick on small and large sprites and
//     stays blocky on point-filtered pixel art.
Shader "Inkform/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.35, 1)
        _OutlineWidth ("Outline Width (texels)", Range(1, 8)) = 3
        _Fade ("Fade", Range(0, 1)) = 0
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
                float4 _MainTex_TexelSize;   // xy = 1/width,1/height — texel-sized sampling steps
                float4 _Color;
                half4  _OutlineColor;
                float  _OutlineWidth;
                float  _Fade;
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

            static const float2 kDirs[8] =
            {
                float2( 1.0,  0.0), float2(-1.0,  0.0), float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 0.70710678,  0.70710678), float2(-0.70710678,  0.70710678),
                float2( 0.70710678, -0.70710678), float2(-0.70710678, -0.70710678)
            };

            half4 Frag(Varyings IN) : SV_Target
            {
                half center = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;

                // Two rings per direction (full width and half) — the half ring fills the diagonal
                // gaps so the band is solid instead of dotted at the corners
                float2 stepFull = _MainTex_TexelSize.xy * _OutlineWidth;
                float2 stepHalf = stepFull * 0.5;
                half ring = 0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 d = kDirs[i];
                    ring = max(ring, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + d * stepFull).a);
                    ring = max(ring, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + d * stepHalf).a);
                }

                // Band strictly outside the silhouette: a pixel with its own alpha keeps none of the glow
                half band = saturate(ring * (1.0 - saturate(center)));

                half4 outline = _OutlineColor * IN.color * _Color;
                outline.a *= band * _Fade;
                return outline;
            }
            ENDHLSL
        }
    }
}
