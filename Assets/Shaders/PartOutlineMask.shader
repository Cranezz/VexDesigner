Shader "VexDesigner/PartOutlineMask"
{
    // Stamps into the stencil buffer wherever a part appears on screen.
    // Draws nothing. Its only job is to let the outline pass tell "edge
    // against the background" from "edge against more of this same part".
    //
    // A separate shader rather than a second pass on the outline shader,
    // because a scriptable render pipeline picks *one* pass per light-mode tag
    // per material. Two untagged passes in one shader meant only the first was
    // ever drawn - which was this one, the invisible one, so the outline
    // vanished entirely.
    //
    // Two materials on the renderer, ordered by render queue, cannot be
    // resolved the wrong way round.
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+1"
        }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Back

            // One bit, so nothing else that uses the stencil buffer is
            // disturbed and neither is this.
            Stencil
            {
                Ref 32
                WriteMask 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
