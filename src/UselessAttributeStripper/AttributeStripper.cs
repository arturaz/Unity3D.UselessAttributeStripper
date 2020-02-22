using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace UselessAttributeStripper {
  public class AttributeStripper {
    readonly Regex[] _attributeRegexes;

    public readonly Dictionary<string, int> StripTotalCountMap = new Dictionary<string, int>();

    public AttributeStripper(Regex[] attributeRegexes) {
      _attributeRegexes = attributeRegexes;
    }

    public Dictionary<string, int> ProcessDll(string dllPath, IAssemblyResolver assemblyResolver) {
      var stripCountMap = new Dictionary<string, int>();

      try {
        var assemblyBytes = File.ReadAllBytes(dllPath);
        using var assemblyReadStream = new MemoryStream(assemblyBytes);
        using var assemblyDef = AssemblyDefinition.ReadAssembly(
          assemblyReadStream, new ReaderParameters {AssemblyResolver = assemblyResolver}
        );
        ProcessAssembly(new[] {assemblyDef}, stripCountMap);

        if (stripCountMap.Count != 0) {
          using var assemblyWriteStream = new MemoryStream();
          assemblyDef.Write(assemblyWriteStream);
          File.WriteAllBytes(dllPath, assemblyWriteStream.GetBuffer());
        }

        return stripCountMap;
      }
      catch (AssemblyResolutionException e) {
        throw new Exception($"Failed to process {dllPath}, you need to pass -d argument to include DLLs", e);
      }
    }

    void ProcessAssembly(AssemblyDefinition[] assemblyDefs, Dictionary<string, int> stripCountMap) {
      foreach (var assemblyDef in assemblyDefs) {
        foreach (var moduleDef in assemblyDef.Modules) {
          foreach (var type in moduleDef.Types)
            RemoveAttributes(type, stripCountMap);
        }
      }
    }

    void RemoveAttributes(TypeDefinition typeDef, Dictionary<string, int> stripCountMap) {
      RemoveAttributes(typeDef.FullName, typeDef.CustomAttributes, stripCountMap);

      foreach (var field in typeDef.Fields)
        RemoveAttributes(field.Name, field.CustomAttributes, stripCountMap);

      foreach (var property in typeDef.Properties)
        RemoveAttributes(property.Name, property.CustomAttributes, stripCountMap);

      foreach (var method in typeDef.Methods)
        RemoveAttributes(method.Name, method.CustomAttributes, stripCountMap);

      foreach (var type in typeDef.NestedTypes)
        RemoveAttributes(type, stripCountMap);
    }

    void RemoveAttributes(
      string ownerName, IList<CustomAttribute> customAttributes, Dictionary<string, int> stripCountMap
    ) {
      foreach (var attrRegex in _attributeRegexes) {
        var idx = 0;
        while (idx < customAttributes.Count) {
          var attr = customAttributes[idx];
          var attrName = attr.Constructor.DeclaringType.FullName;
          if (attr.Constructor != null && attrRegex.IsMatch(attrName)) {
            customAttributes.RemoveAt(idx);

            stripCountMap.TryGetValue(attrName, out var count);
            stripCountMap[attrName] = count + 1;

            lock (StripTotalCountMap) {
              StripTotalCountMap.TryGetValue(attrName, out var totalCount);
              StripTotalCountMap[attrName] = totalCount + 1;
            }
          }
          else {
            idx++;
          }
        }
      }
    }
  }
}