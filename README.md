# Unity3D.UselessAttributeStripper

For iOS application, Unity3D uses [IL2CPP](http://blogs.unity3d.com/kr/2015/05/06/an-introduction-to-ilcpp-internals/)
for translating your .NET IL code to native one,
which makes the size of your executable much larger than you expect.
It's not easy problem to make it smaller. However, it is mandatory to keep the size
under 100MB to allow iOS users to download your app over the air.

This tools will give you small margin to shrink your app a little.
IL2CPP makes a small code for every attribute in your app assembly. It's small.
But with thousands of .NET attributes, it may bloat your code size.
This tool provide a way to remove useless attributes from your app,
which doens't affect running at all.
(For detailed information, read [Under The Hood](./docs/UnderTheHood.md).)

The margin depends on your code patern. For my project which heavily exploited coroutines,
it could remove 17,000 attributes from whole assemblies and reduced the uncompressed app size
to 126MB from 144MB (-18MB). It seemed small but because code area in app
will be encrypted before compression, this size really mattered.
(For detailed information of this case, read [Sample Case](./docs/SampleCase.md).)

### Setup

Read the source. Build from source. Run manually. Enjoy.

Example for Unity 2019.3.1:

```
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using com.tinylabproductions.TLPLib.Data;
using com.tinylabproductions.TLPLib.Extensions;
using pzd.lib.exts;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Game.code.Editor.hooks {
  public class StripUselessAttributesBuildHook : IPostBuildPlayerScriptDLLs {
    public static void strip(IEnumerable<string> dllPaths) {
      var tool = Path.GetFullPath("Tools/Compilation/useless-attribute-stripper/UselessAttributeStripper.exe");
      var stripAttributesXml = Path.GetFullPath("Assets/strip-attributes.xml");
      var logFile = Path.GetFullPath("strip-attributes.log");
      var fullDllPaths = dllPaths.Select(s => $"-a \"{Path.GetFullPath(s)}\"").mkString(" ");
      var editorData = EditorApplication.applicationContentsPath;
      // We need to trim the '\' from the end because otherwise it escapes quotes. 
      var dependencies = new[] {
        "Assets/Plugins/quantum", $"{editorData}/Managed", $"{editorData}/Managed/UnityEngine"
      }.Select(s => $"-d \"{Path.GetFullPath(s).TrimEnd('\\')}\"").mkString(" ");
      var args = 
        $"-x \"{stripAttributesXml}\" " +
        $"-l \"{logFile}\" " +
        dependencies + " " +
        fullDllPaths;
      log($"Stripping: {tool} {args}");
      strip(tool, args);
    }

    static void strip(string tool, string args) {
      // TODO: mac os support
      try {
        var p = Process.Start(new ProcessStartInfo(tool, args) {
          UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardError = true, RedirectStandardInput = true, RedirectStandardOutput = true
        });
        if (p == null) {
          logErr($"Running '{tool} {args}' failed: process is null");
        }
        else
          using (p) {
            p.WaitForExit(1.minute().millis);
            var stdout = p.StandardOutput.ReadToEnd();
            var maybeStderr = p.StandardError.ReadToEnd().nonEmptyOpt(true);

            log($"Exit code: {p.ExitCode}");
            log($"STDOUT:\n{stdout}");
            foreach (var stdErr in maybeStderr) logErr($"STDERR:\n{stdErr}");
          }
      }
      catch (Exception e) {
        logErr($"Running '{tool} {args}' failed: {e}");
      }
    }

    static void log(string s) => Debug.Log($"[{nameof(StripUselessAttributesBuildHook)}] {s}");
    static void logErr(string s) => Debug.LogError($"[{nameof(StripUselessAttributesBuildHook)}] {s}");
    
    public int callbackOrder => 0;
    public void OnPostBuildPlayerScriptDLLs(BuildReport report) {
      log(nameof(OnPostBuildPlayerScriptDLLs));
      var dlls = report.files
        .Select(f => f.path)
        .Where(f => f.EndsWithFast(".dll", true) && Path.GetFileName(f) != "System.dll")
        .ToArray();
      strip(dlls);
    }
  }
}
```

