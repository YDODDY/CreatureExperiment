// Draws nothing. For navigation-only geometry: the MeshRenderer stays enabled so
// NavMeshSurface (Use Geometry = Render Meshes) collects it, but it is never visible
// and casts no shadow (no ShadowCaster / DepthOnly pass).
Shader "Hidden/NavOnlyInvisible"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZWrite Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(2, 2, 2, 1); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
