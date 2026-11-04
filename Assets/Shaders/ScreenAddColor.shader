// 화면 전체에 같은 색을 더한다(가산). VillageNightTint가 카메라 앞에 둔 사각형에 쓴다.
// 보이는 곳과 안 보이는 곳에 똑같이 더하므로 어둠 속 실루엣이 생기지 않는다(시야 밖 정보가 새지 않음).
Shader "Animal Keeper/Screen Add Color"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _Color("Add Color", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); return o; }
            half4 frag(V i) : SV_Target { return half4(_Color.rgb, 0); }
            ENDHLSL
        }
    }
}
