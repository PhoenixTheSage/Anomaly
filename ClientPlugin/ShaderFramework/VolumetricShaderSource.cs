using System;
using System.IO;
using System.Text;
using ClientPlugin.Shaders;

namespace ClientPlugin.ShaderFramework;

/// <summary>Host-owned shader composition; providers export EvaluateMedium in a local origin frame.</summary>
internal static class VolumetricShaderSource
{
    internal static string Build(string includes,VolumetricMediumRegistry.Medium[] media,string kernel)
    {
        var result=new StringBuilder();
        result.AppendLine("#include \""+Path.Combine(includes,"AnomalyVolumeCommon.hlsli").Replace('\\','/')+"\"");
        for(int i=0;i<media.Length;i++)
        {
            result.AppendLine("#define EvaluateMedium VolumeProvider"+i);
            result.AppendLine("#define MediumUniforms VolumeUniforms["+i+"]");
            result.AppendLine("#define MediumCameraPosition (-VolumeOrigins["+i+"].xyz)");
            result.AppendLine("#define MediumVelocity VolumeProviderMotion["+i+"].xyz");
            for(int j=0;j<3;j++) {
                result.AppendLine("#define MediumTexture"+j+" VolumeTexture"+i+"_"+j);
                result.AppendLine("#define MediumRegister"+j+" t"+(32+i*3+j));
            }
            result.AppendLine("#include \""+media[i].ShaderFile.Replace('\\','/')+"\"");
            result.AppendLine("#undef EvaluateMedium\n#undef MediumUniforms\n#undef MediumVelocity\n#undef MediumCameraPosition");
            for(int j=0;j<3;j++) result.AppendLine("#undef MediumTexture"+j+"\n#undef MediumRegister"+j);
        }
        result.AppendLine("AnomalyMediumSample VolumeGetMedium(uint index,float3 p) { AnomalyMediumSample m=AnomalyEmptyMedium();");
        result.AppendLine("if(any(p<VolumeBoundsMin[index].xyz)||any(p>VolumeBoundsMax[index].xyz)) return m;");
        for(int i=0;i<media.Length;i++) result.AppendLine("if(index=="+i+") m=VolumeProvider"+i+"(p-VolumeOrigins["+i+"].xyz);");
        result.AppendLine("if(!isfinite(m.extinction)||any(!isfinite(m.scattering))||any(!isfinite(m.emission))||!isfinite(m.anisotropy)||any(!isfinite(m.velocity))) return AnomalyEmptyMedium(); return m; }");
        result.AppendLine(File.ReadAllText(Path.Combine(includes,"AnomalyVolumeEvaluate.hlsli")));
        result.AppendLine(File.ReadAllText(Path.Combine(includes,kernel)));
        return result.ToString();
    }
}
