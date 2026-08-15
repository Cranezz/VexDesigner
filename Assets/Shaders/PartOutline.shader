Shader "VexDesigner/PartOutline"
{
    // Traces a part's silhouette: a coloured border where the part meets the
    // background, and nowhere else.
    //
    // Pairs with VexDesigner/PartOutlineMask, which stamps the part into the
    // stencil buffer first. Geometry alone cannot tell "edge against the sky"
    // from "edge against more of this same part", so a plain inverted-hull
    // outline ringed all 174 holes of a C-channel and read as a wireframe.
    // The stencil can: this pass draws only where the stamp is absent, so a
    // hole showing the workshop behind it gets a border and a hole showing the
    // channel's own far wall does not. Folds and seams stay clean.
    //
    // Chosen over an emissive glow because an outline says something a glow
    // cannot. A frozen part is *marked*, not lit: the mark has to be legible
    // against pale aluminium and dark shadow alike, has to leave the part's
    // own colour readable, and must not be confused with the part catching the
    // light. Emission failed all three.
    Properties
    {
        _BaseColor ("Colour", Color) = (0.2, 0.55, 1, 1)
        _Thickness ("Thickness (pixels)", Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+2"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Front faces culled, so what remains is the back of the fattened
            // hull - which pokes out past the part exactly at its silhouette.
            Cull Front
            ZWrite Off
            ZTest LEqual

            // Only outside the stamp. This is what confines the border to the
            // outside edge instead of ringing every hole and fold.
            Stencil
            {
                Ref 32
                ReadMask 32
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;

                // Smoothed normals, baked at import. See BakeOutlineNormals.
                // Extruding along the *rendering* normals tears the border open
                // at every hard edge, because those are deliberately split so
                // machined corners stay crisp.
                float3 smoothOS   : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Thickness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Fall back to the rendering normal on a mesh that has not been
                // baked, so an unprocessed part still gets a border - a seamed
                // one, but visible.
                float3 extrude = any(input.smoothOS) ? input.smoothOS : input.normalOS;

                // Pushed out in clip space rather than along the normal in
                // world space, which is what keeps the border a constant width
                // on screen however far away the part is.
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP,
                                      mul((float3x3)UNITY_MATRIX_M, extrude));

                float2 offset = normalize(normalCS.xy);
                offset *= _Thickness * 2.0 / _ScreenParams.xy;

                output.positionCS.xy += offset * output.positionCS.w;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
