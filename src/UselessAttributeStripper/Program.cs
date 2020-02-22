using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Mono.Cecil;

namespace UselessAttributeStripper {
  internal static class Program {
    static int Main(string[] args) {
      if (args.Length == 0) {
        ShowUsage();
        return 1;
      }

      Log("================================================================================", null);
      Log("Start! " + DateTime.Now, null);
      Log("================================================================================", null);
      DumpArgs(args, null);

      var resolver = new DefaultAssemblyResolver();
      LoadConfiguration(
        args, out var attributeNames, out var dllFileNames, out var parallel, out var logFile, resolver
      );
      if (attributeNames.Count == 0) {
        throw new Exception("No attributes to strip! Try specifying '-x path-to-strip-attributes.xml'");
      }
      else {
        foreach (var attributeName in attributeNames) {
          Log($"CustomAttribute: {attributeName}", logFile);
        }
      }

      if (dllFileNames.Count == 0) {
        throw new Exception("No DLLs to strip! Try specifying '-a some.dll'");
      }

      ProcessStrip(attributeNames, dllFileNames, resolver, parallel, logFile);

      Log("Done!", logFile);
      return 0;
    }

    static void ShowUsage() {
      Console.WriteLine("Useless Attribute Stripper for Unity3D IL2CPP");
      Console.WriteLine("https://github.com/arturaz/Unity3D.UselessAttributeStripper");
      Console.WriteLine("-p turns on parallel processing");
      Console.WriteLine("-x config.xml");
      Console.WriteLine("-a i-want-to-be-stripped.dll (can be repeated)");
      Console.WriteLine("-d directory_with_dlls_for_the_assembly_resolver (can be repeated)");
    }

    static void DumpArgs(string[] args, string? logFile) {
      Log($"Exe: {Assembly.GetExecutingAssembly().Location}", logFile);
      Log($"Path: {Environment.CurrentDirectory}", logFile);

      Log("---- ARGS -----", logFile);
      for (var i = 0; i < args.Length; i++)
        Log($"args[{i}]='{args[i]}'", logFile);
      Log("---------------", logFile);
    }

    static void LoadConfiguration(
      string[] args, out List<Regex> attributeNames, out List<string> dllFileNames, out bool parallel,
      out string? logFile, BaseAssemblyResolver assemblyResolver
    ) {
      attributeNames = new List<Regex>();
      dllFileNames = new List<string>();
      parallel = false;
      logFile = null;
      for (var i = 0; i < args.Length; i++) {
        switch (args[i]) {
          case "-l":
            logFile = args[i + 1];
            i++;
            break;
          
          case "-a":
            // eg: -a ./Client/Temp/StagingArea/Data/Managed/Assembly-CSharp.dll
            dllFileNames.Add(args[i + 1]);
            i += 1;
            break;

          case "-d":
            assemblyResolver.AddSearchDirectory(args[i + 1]);

            i += 1;
            break;
          
          case "-p":
            parallel = true;
            break;

          case "-x":
            // eg: -x ./Client/Assets/link.xml
            var xmlFile = args[i + 1];
            if (File.Exists(xmlFile)) {
              try {
                var xml = XDocument.Load(xmlFile);
                var attrRoot = xml.Element("strip-attribute");
                if (attrRoot == null)
                  throw new Exception("Can't find root <strip-attribute> tag in the XML.");

                var elements = attrRoot.Elements("type").ToArray();
                for (var idx = 0; idx < elements.Length; idx++) {
                  var typeElement = elements[idx];
                  var regex = typeElement.Attribute("regex");
                  if (regex == null)
                    throw new Exception($"Can't find regex attribute on <type> index {idx}");
                  attributeNames.Add(new Regex(regex.Value));
                }
              }
              catch (Exception e) {
                throw new Exception($"Parsing XML file error. File={xmlFile}", e);
              }
            }
            else {
              throw new Exception($"File does not exist: {xmlFile}");
            }

            i += 1;
            break;
        }
      }
    }

    static void ProcessStrip(
      List<Regex> attributeNames, List<string> dllFileNames, IAssemblyResolver resolver,
      bool parallel, string? logFile
    ) {
      var stripper = new AttributeStripper(attributeNames.ToArray());

      // let's strip attribute fom dlls!
      if (parallel) Parallel.ForEach(dllFileNames, strip);
      else dllFileNames.ForEach(strip);

      // show summary

      Log("* Summary * ", logFile);
      foreach (var item in stripper.StripTotalCountMap.OrderByDescending(i => i.Value))
        Log($"  - {item.Key} : {item.Value}", logFile);

      void strip(string dllFileName) {
        if (!File.Exists(dllFileName))
          throw new Exception($"File does not exist: {dllFileName}");

        var stripCountMap = stripper.ProcessDll(dllFileName, resolver);
        var logStr = "- ProcessDll : " + dllFileName + "\n" + string.Join(
          "\n",
          stripCountMap.OrderByDescending(i => i.Value).Select(item => $"  - {item.Key} : {item.Value}")
        );           
        Log(logStr, logFile);
      }
    }

    static readonly object logLock = new object();
    static void Log(string log, string? logFile) {
      lock(logLock) {
        Console.WriteLine(log);
        if (logFile != null) File.AppendAllText(logFile, log);
      }
    }
  }
}