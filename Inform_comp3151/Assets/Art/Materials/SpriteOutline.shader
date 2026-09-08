// Inkform sprite-outline — URP 2D sprite edge glow for interaction prompts. A second SpriteRenderer
// (scaled up by the prompt's padding, same sprite) draws with this material: it discards the body
// and paints a soft, additive glow hugging the alpha silhouette (8 directions x 4 weighted
// sample rings in texel space), so the light fades out away from the sprite's edge.
//
// A sprite mesh never has drawable pixels outside its silhouette, so the quad is scaled up around
// the sprite and the fragment UVs are remapped back into sprite space (_SpriteRect/_SpriteScale):
// the sprite still renders at its original size while the padding ring around it becomes free
// space for the band. Samples outside the sprite's UV rect are masked out — clamped edge reads
// would otherwise smear the texture border into the glow.
//
// Driven by InteractionPromptPart:
//   • _Fade (0..1) is the fade-in/out of the whole glow (plus the breathing pulse), set per frame;
//   • _OutlineWidth is in texels of the sprite texture; widen _SpriteRect's padding (the prompt's
//     Outline Padding field) together with it so the band has room.
Shader "Inkform/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [HDR] _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.35, 1)
        _OutlineWidth ("Outline Width (texels)", Range(1, 8)) = 4
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 2
        _Fade ("Fade", Range(0, 1)) = 0

        // Runtime-driven by InteractionPromptPart; defaults cover the whole texture
        _SpriteRect ("Sprite UV Rect (x0, y0, x1, y1)", Vector) = (0, 0, 1, 1)
        _SpriteScale ("Outline Quad Scale", Float) = 1
        _SpriteUvStep ("Sprite UV Step", Vector) = (1, 1, 1, 1)
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
        // Add light without darkening the background or changing the render target's alpha.
        Blend SrcAlpha One, Zero One

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                half4  _OutlineColor;
                float  _OutlineWidth;
                float  _GlowIntensity;
                float  _Fade;
                float4 _SpriteRect;
                float  _SpriteScale;
                float4 _SpriteUvStep;        // xy = 1/width,1/height, supplied by the component
            CBUFFER_END

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            Varyings Vert(Attributes IN)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(IN);
                SetUpSpriteInstanceProperties();
                IN.positionOS = UnityFlipSprite(IN.positionOS, unity_SpriteProps.xy);

                Varyings OUT = CommonUnlitVertex(IN);
                OUT.color = IN.color * _Color * unity_SpriteColor;
                return OUT;
            }

            static const float2 kDirs[8] =
            {
                float2( 1.0,  0.0), float2(-1.0,  0.0), float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 0.70710678,  0.70710678), float2(-0.70710678,  0.70710678),
                float2( 0.70710678, -0.70710678), float2(-0.70710678, -0.70710678)
            };

            // Alpha masked to the sprite's own UV rect: reads outside it (the padding margin, or a
            // clamped fetch past the rect edge) contribute nothing instead of smearing the border
            half MaskedAlpha(float2 uv)
            {
                float2 inside = step(_SpriteRect.xy, uv) * step(uv, _SpriteRect.zw);
                float2 halfTexel = _SpriteUvStep.xy * 0.5;
                float2 sampleUV = clamp(uv, _SpriteRect.xy + halfTexel, _SpriteRect.zw - halfTexel);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).a * inside.x * inside.y;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Undo the quad's padding scale: the fragment maps back to where the unscaled sprite
                // mesh would have sampled, so the silhouette lands at its original size/position
                float2 rectCenter = (_SpriteRect.xy + _SpriteRect.zw) * 0.5;
                float2 suv = rectCenter + (IN.uv - rectCenter) * _SpriteScale;

                // Nearby samples contribute more light than distant ones. Combining four rings
                // gives a bright rim that fades outward instead of a solid, hard-edged stroke.
                float2 stepFull = _SpriteUvStep.xy * max(_OutlineWidth, 0.0);
                half glow = 0;
                [unroll]
                for (int radius = 1; radius <= 4; radius++)
                {
                    half ring = 0;
                    [unroll]
                    for (int i = 0; i < 8; i++)
                    {
                        float2 offset = kDirs[i] * stepFull * (radius * 0.25);
                        ring = max(ring, MaskedAlpha(suv + offset));
                    }
                    float weight = 1.0 - smoothstep(0.0, 1.0, (radius - 0.5) * 0.25);
                    glow += ring * weight;
                }
                half center = MaskedAlpha(suv);

                // The four weights sum to two. Keep the glow outside the sprite body.
                half band = saturate(glow * 0.5) * (1.0 - saturate(center));

                half4 outline = _OutlineColor * IN.color;
                outline.rgb *= _GlowIntensity;
                outline.a *= band * saturate(_Fade);
                return outline;
            }
            ENDHLSL
        }
    }
}
