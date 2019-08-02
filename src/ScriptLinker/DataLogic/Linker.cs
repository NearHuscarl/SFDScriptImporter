﻿using ScriptLinker.Models;
using ScriptLinker.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ScriptLinker.DataLogic
{
    public class Linker
    {
        private readonly string projectName = Assembly.GetCallingAssembly().GetName().Name;
        private readonly string breakpointStatement = "System.Diagnostics.Debugger.Break();";

        // It will print something like this:
        // 
        // // --------------------------------------------
        // // File: E:\Github\SFDScript\BotExtended\Bot.cs
        // // --------------------------------------------
        private string GetFileHeader(string filePath, string projectPath)
        {
            var relativePath = filePath.Substring(projectPath.Length + 1);
            var filePathLength = relativePath.Length;
            var bar = new string('-', filePathLength + 6);
            return $@"
// {bar}
// File: {relativePath}
// {bar}{Environment.NewLine}";
        }

        private string GetHeader(ScriptInfo info)
        {
            var sb = new StringBuilder();
            var dateNow = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");

            sb.AppendLine($"// This file is auto generated by {projectName} at {dateNow}");
            sb.AppendLine("// Sauce: https://github.com/NearHuscarl/ScriptLinker");
            sb.AppendLine();
            sb.AppendLine("/*");
            sb.AppendLine($"* author: {info.Author}");
            sb.AppendLine($"* description: {info.Description}");
            sb.AppendLine($"* mapmodes: {info.MapModes}");
            sb.AppendLine("*/");

            return sb.ToString();
        }

        public LinkResult Link(ProjectInfo projectInfo, ScriptInfo scriptInfo)
        {
            var linkedFiles = new HashSet<string>();
            var usingNamespaces = new HashSet<string>();
            var newNamespaces = new List<string>();
            var sb = new StringBuilder();

            sb.Append(GetHeader(scriptInfo));

            var entryPointInfo = ReadEntryPointFile(projectInfo);

            sb.AppendLine(entryPointInfo.Content);
            linkedFiles.Add(projectInfo.EntryPoint);
            newNamespaces = entryPointInfo.UsingNamespaces
                .Where(ns => ns.StartsWith(projectInfo.RootNamespace))
                .Select(ns => ns.Trim()).ToList();

            while (newNamespaces.Any())
            {
                var addedNamespaces = new List<string>();
                foreach (var file in FileUtil.GetScriptFiles(projectInfo.ProjectDir))
                {
                    if (linkedFiles.Contains(file)) continue;

                    var ns = FileUtil.GetNamespace(file);
                    if (newNamespaces.Contains(ns))
                    {
                        var fileInfo = ReadCSharpFile(projectInfo, file, entryPointInfo.Namespace);

                        sb.Append(fileInfo.Content);
                        addedNamespaces.AddRange(fileInfo.UsingNamespaces);
                        linkedFiles.Add(file);
                    }
                }

                newNamespaces = addedNamespaces;

                foreach (var newNS in newNamespaces.ToList())
                {
                    if (!usingNamespaces.Contains(newNS) && newNS.StartsWith(projectInfo.RootNamespace))
                        usingNamespaces.Add(newNS.Trim());
                    else
                        newNamespaces.Remove(newNS);
                }
            }

            sb.AppendLine();
            return new LinkResult()
            {
                Content = sb.ToString(),
                LinkedFiles = linkedFiles,
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="projectInfo"></param>
        /// <param name="filePath">Specify if it's a partial class of the entrypoint class</param>
        /// <returns></returns>
        public CSharpFileInfo ReadEntryPointFile(ProjectInfo projectInfo, string filePath = "")
        {
            if (filePath == "")
            {
                filePath = projectInfo.EntryPoint;
            }

            var sourceCode = new StringBuilder();
            var usingNamespaces = new List<string>();
            var fileNamespace = "";
            var breakpoints = projectInfo.Breakpoints
                .Where(b => b.File == filePath)
                .ToDictionary(b => b.LineNumber, b => b);

            sourceCode.AppendLine(GetFileHeader(filePath, projectInfo.ProjectDir));

            using (var file = File.OpenText(filePath))
            {
                var line = "";
                var codeBlock = 0;
                var lineNumber = 1;

                while ((line = file.ReadLine()) != null)
                {
                    if (codeBlock <= 1)
                    {
                        var match = RegexPattern.UsingStatement.Match(line);
                        if (match.Success)
                            usingNamespaces.Add(match.Groups[1].Value);

                        match = RegexPattern.Namespace.Match(line);
                        if (match.Success)
                        {
                            fileNamespace = match.Groups[1].Value;
                            usingNamespaces.Add(fileNamespace);
                        }
                    }

                    var commentIndex = line.IndexOf("//"); // -1 if not found

                    if (commentIndex == -1)
                        commentIndex = line.Length;

                    var startBlock = false; // if current line has character '{' (not in comment)
                    var endBlock = false; // if current line has character '}' (not in comment)

                    for (var i = 0; i < line.Length; i++)
                    {
                        if (i > commentIndex)
                            break;

                        if (line[i] == '{') startBlock = true;
                        if (line[i] == '}') endBlock = true;
                    }

                    if (startBlock)
                        codeBlock++;

                    if (RegexPattern.Constructor.Match(line).Success)
                    {
                        goto Finish;
                    }

                    if (codeBlock == 2 && !startBlock && !endBlock)
                    {
                        sourceCode.AppendLine(line);
                    }
                    if (codeBlock > 2)
                    {
                        sourceCode.AppendLine(line);
                    }
                    if (breakpoints.ContainsKey(lineNumber))
                    {
                        sourceCode.AppendLine(breakpointStatement);
                    }

                    Finish:
                    if (endBlock)
                        codeBlock--;

                    lineNumber++;
                }
            }

            return new CSharpFileInfo()
            {
                Content = sourceCode.ToString(),
                UsingNamespaces = usingNamespaces,
                Namespace = fileNamespace,
            };
        }

        public CSharpFileInfo ReadCSharpFile(ProjectInfo projectInfo, string filePath, string entryPointNamespace)
        {
            var sourceCode = new StringBuilder();
            var usingNamespaces = new List<string>();
            var fileNamespace = "";
            var breakpoints = projectInfo.Breakpoints
                .Where(b => b.File == filePath)
                .ToDictionary(b => b.LineNumber, b => b);

            sourceCode.AppendLine(GetFileHeader(filePath, projectInfo.ProjectDir));

            using (var file = File.OpenText(filePath))
            {
                var line = "";
                var codeBlock = 0;
                var lineNumber = 1;

                while ((line = file.ReadLine()) != null)
                {
                    if (codeBlock <= 1)
                    {
                        var match = RegexPattern.UsingStatement.Match(line);
                        if (match.Success)
                            usingNamespaces.Add(match.Groups[1].Value);

                        match = RegexPattern.Namespace.Match(line);
                        if (match.Success)
                        {
                            fileNamespace = match.Groups[1].Value;
                            usingNamespaces.Add(fileNamespace);
                        }
                    }

                    if (RegexPattern.EntryPointClass.Match(line).Success)
                    {
                        if (fileNamespace == entryPointNamespace && RegexPattern.PartialClass.Match(line).Success)
                        {
                            return ReadEntryPointFile(projectInfo, filePath);
                        }
                        else
                            return new CSharpFileInfo(); // Only import normal c# file. Ignore another game script unless it's a partial class
                    }

                    if (string.IsNullOrEmpty(fileNamespace))
                    {
                        var match = RegexPattern.Namespace.Match(line);
                        if (match.Success)
                            fileNamespace = match.Groups[1].Value;
                    }

                    var commentIndex = line.IndexOf("//"); // -1 if not found

                    if (commentIndex == -1)
                        commentIndex = line.Length;

                    var startBlock = false; // if current line has character '{' (not in comment)
                    var endBlock = false; // if current line has character '}' (not in comment)

                    for (var i = 0; i < line.Length; i++)
                    {
                        if (i > commentIndex)
                            continue;

                        if (line[i] == '{') startBlock = true;
                        if (line[i] == '}') endBlock = true;
                    }

                    if (startBlock)
                        codeBlock++;

                    if (codeBlock == 1 && !startBlock && !endBlock)
                    {
                        sourceCode.AppendLine(line);
                    }
                    if (codeBlock > 1)
                    {
                        sourceCode.AppendLine(line);
                    }
                    if (breakpoints.ContainsKey(lineNumber))
                    {
                        sourceCode.AppendLine(breakpointStatement);
                    }

                    if (endBlock)
                        codeBlock--;

                    lineNumber++;
                }
            }

            return new CSharpFileInfo()
            {
                Content = sourceCode.ToString(),
                UsingNamespaces = usingNamespaces,
                Namespace = fileNamespace,
            };
        }
    }
}
