using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Rooftop.Editor
{
    public static class PracticeBuild
    {
        public static void Build()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[]{"Assets/Rooftop/VentRoof.unity"},
                locationPathName = Environment.GetEnvironmentVariable("ROOF_BUILD") ?? "Builds/Rooftop.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Practice build failed");
        }
    }
}
