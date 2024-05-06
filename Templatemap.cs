﻿using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodeGen.Helpers;
using CodeGen.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CodeGen;

internal static class TemplateMap
{
    private static readonly string _folder;
    private static readonly List<string> _templateFiles = [];
    private const string _defaultExt = ".txt";
    private const string _templateDir = ".templates";
    private const string _defaultNamespace = "Triangle.LMS";
    static TemplateMap()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userProfile = Path.Combine(folder, ".vs", _templateDir);

        if (Directory.Exists(userProfile))
        {
            _templateFiles.AddRange(Directory.GetFiles(userProfile, "*" + _defaultExt, SearchOption.AllDirectories));
        }

        var assembly = Assembly.GetExecutingAssembly().Location;
        _folder = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
        _templateFiles.AddRange(Directory.GetFiles(_folder, "*" + _defaultExt, SearchOption.AllDirectories));
    }


    public static async Task<string> GetTemplateFilePathAsync(Project project, IntellisenseObject classObject, string file, string itemname, string selectFolder)
    {
        var templateFolders = new string[]{
           // "Queries\\GetAll",
            "Model",
            "Repository",
            "Service",
            "Validator",
            "CoreResolver",
            "Infrastructure",
            "Controllers"
            };
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var name = Path.GetFileName(file);
        var safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
        var relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");
        var selectRelative = PackageUtilities.MakeRelative(project.GetRootFolder(), selectFolder ?? "");
        string templateFile = null;
        var list = _templateFiles.ToList();

        AddTemplatesFromCurrentFolder(list, Path.GetDirectoryName(file));




        // Look for direct file name matches
        if (list.Any(f =>
        {
            var pattern = templateFolders
                .Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)
                .First()
                .Replace("\\", "\\\\");
            var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
            return result;

        }))
        {

            var tampFile = list.OrderByDescending(x => x.Length).FirstOrDefault(f =>
            {
                var pattern = templateFolders
                    .Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)
                    .First()
                    .Replace("\\", "\\\\");

                var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);

                if (result)
                {
                    var fileName = Path.GetFileNameWithoutExtension(f)
                          .Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                          .All(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

                    return fileName;
                }
                return false;
            });
            templateFile = tampFile;
        }

        // Look for file extension matches
        else if (list.Any(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase)))
        {
            var tempFile = list.FirstOrDefault(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
            var tamp = AdjustForSpecific(safeName, extension);
            templateFile = Path.Combine(Path.GetDirectoryName(tempFile), tamp + _defaultExt);
        }
        var modifiedRelativePath = CodeGenPackage.RemoveFolderNameFromFile(relative, 0);
        var template = await ReplaceTokensAsync(project, classObject, itemname, modifiedRelativePath, selectRelative, templateFile);
        return NormalizeLineEndings(template);
    }

    private static void AddTemplatesFromCurrentFolder(List<string> list, string dir)
    {
        var current = new DirectoryInfo(dir);
        var dynaList = new List<string>();
        while (current != null)
        {
            var tempDir = Path.Combine(current.FullName, _templateDir);

            if (Directory.Exists(tempDir))
            {
                dynaList.AddRange(Directory.GetFiles(tempDir, "*" + _defaultExt, SearchOption.AllDirectories));
            }
            current = current.Parent;
        }
        list.InsertRange(0, dynaList);
    }

    private static async Task<string> ReplaceTokensAsync(Project project, IntellisenseObject classObject, string name, string relative, string selectRelative, string templateFile)
    {
        if (string.IsNullOrEmpty(templateFile))
        {
            return templateFile;
        }

        var domainRootNs = CodeGenPackage.CoreRootNs;
        var infrastructureRootNs = CodeGenPackage.InfrastructureRootNs;
        var apiRootNs = CodeGenPackage.ApiRootNs;

        var rootNs = project.GetRootNamespace();
        var ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;
        var selectNs = ns;
        if (!string.IsNullOrEmpty(relative))
        {
            ns += "." + ProjectHelpers.CleanNameSpace(relative);
        }
        if (!string.IsNullOrEmpty(selectRelative))
        {
            selectNs += "." + ProjectHelpers.CleanNameSpace(selectRelative);
            selectNs = selectNs.Remove(selectNs.Length - 1);
        }
        using var reader = new StreamReader(templateFile);
        var content = await reader.ReadToEndAsync();
        var nameofPlural = ProjectHelpers.Pluralize(name);
        var dtoFieldDefinition = CreateDtoFieldDefinition(classObject);
        var ParameterDefinition = CreateParameterDefinition(classObject);
        var templateFieldDefinition = CreateTemplateFieldDefinition(classObject);
        var fieldAssignmentDefinition = CreateFieldAssignmentDefinition(classObject);

        return content.Replace("{rootnamespace}", _defaultNamespace)
                        .Replace("{namespace}", ns)
                        .Replace("{selectns}", selectNs)
                        .Replace("{itemName}", name)
                        .Replace("{itemNameUpper}", name.ToUpper())
                        .Replace("{itemNameLower}", name.ToLower())
                        .Replace("{nameofPlural}", nameofPlural)
                        .Replace("{dtoFieldDefinition}", dtoFieldDefinition)
                        .Replace("{parameterDefinition}", ParameterDefinition)
                        .Replace("{fieldAssignmentDefinition}", fieldAssignmentDefinition)
                        .Replace("{templateFieldDefinition}", templateFieldDefinition)
                        .Replace("{domainRootNs}", domainRootNs)
                        .Replace("{infrastructureRootNs}", infrastructureRootNs)
                        .Replace("{apiRootNs}", apiRootNs)
                        ;
    }

    private static string NormalizeLineEndings(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
    }

    private static string AdjustForSpecific(string safeName, string extension)
    {
        if (Regex.IsMatch(safeName, "^I[A-Z].*"))
        {
            return extension += "-interface";
        }

        return extension;
    }
    private static string SplitCamelCase(string str)
    {
        var r = new Regex(@"
                (?<=[A-Z])(?=[A-Z][a-z]) |
                 (?<=[^A-Z])(?=[A-Z]) |
                 (?<=[A-Za-z])(?=[^A-Za-z])", RegexOptions.IgnorePatternWhitespace);
        return r.Replace(str, " ");
    }
    public const string PRIMARYKEY = "Id";
    private static string CreateDtoFieldDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
        {
            if (property.Name == PRIMARYKEY)
            {
                output.Append($"    public {property.Type.CodeName} {property.Name} {{ get;set; }} \r\n");
            }
            else
            {
                switch (property.Type.CodeName)
                {
                    case "string" when property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase):
                        output.Append($"public {property.Type.CodeName} {property.Name} {{ get;set; }} = string.Empty; \r\n");
                        break;
                    case "string" when !property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) && !property.Type.IsArray && !property.Type.IsDictionary:
                        output.Append($"public {property.Type.CodeName}? {property.Name} {{ get;set; }} \r\n");
                        break;
                    case "string" when !property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) && property.Type.IsArray:
                        output.Append($"    public HashSet<{property.Type.CodeName}>? {property.Name} {{ get;set; }} \r\n");
                        break;
                    case "System.DateTime?":
                        output.Append($"    public DateTime? {property.Name} {{ get;set; }} \r\n");
                        break;
                    case "System.DateTime":
                        output.Append($"    public DateTime {property.Name} {{ get;set; }} \r\n");
                        break;
                    case "decimal?":
                    case "decimal":
                    case "int?":
                    case "int":
                    case "double?":
                    case "double":
                        output.Append($"    public {property.Type.CodeName} {property.Name} {{ get;set; }} \r\n");
                        break;
                    default:
                        if (property.Type.CodeName.Any(x => x == '?'))
                        {
                            output.Append($"    public {property.Type.CodeName} {property.Name} {{ get;set; }} \r\n");
                        }
                        else
                        {
                            if (property.Type.IsOptional)
                            {
                                output.Append($"    public {property.Type.CodeName}? {property.Name} {{ get;set; }} \r\n");
                            }
                            else
                            {
                                output.Append($"    public {property.Type.CodeName} {property.Name} {{ get;set; }} \r\n");
                            }
                        }
                        break;
                }

            }
        }
        return output.ToString();
    }

    private static string CreateParameterDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
        {
            var parameterName = string.Join(string.Empty, property.Name
                 .Select(c => new string(c, 1)).Select(c => c[0] < 'Z' ? "_" + c : c)).Trim().ToUpper();

            switch (property.Type.CodeName)
            {
                case "string":
                    output.Append($"\r\n                         .AddVarchar2Parameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "System.DateTime?":
                    output.Append($"\r\n                         .AddDateTimeParameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "System.DateTime":
                    output.Append($"\r\n                         .AddDateTimeParameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "bool":
                    output.Append($"\r\n                         .AddBoolCharParameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "int":
                case "int?":
                    output.Append($"\r\n                         .AddIntParameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "int64":
                    output.Append($"\r\n                         .AddLongParameter(\"P{parameterName}\",  entity.{property.Name})");
                    break;
                case "decimal?":
                case "decimal":
                case "double?":
                case "double":
                    output.Append($"\r\n                         .AddDecimalParameter(\"P{parameterName}\", entity.{property.Name})");
                    break;
                default:
                    output.Append($"\r\n                         .AddIntParameter(\"P{parameterName}\",  (int)entity.{property.Name})");
                    break;
            }
        }
        return output.ToString();
    }
    private static string CreateTemplateFieldDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
        {
            if (property.Name == PRIMARYKEY) continue;
            output.Append($"_localizer[_dto.GetMemberDescription(x=>x.{property.Name})], \r\n");
        }
        return output.ToString();
    }

    private static string CreateMudTdHeaderDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        var defaultFieldName = new string[] { "Name", "Description" };
        if (classObject.Properties.Where(x => x.Type.IsKnownType == true && defaultFieldName.Contains(x.Name)).Any())
        {
            output.Append($"<PropertyColumn Property=\"x => x.Name\" Title=\"@L[_currentDto.GetMemberDescription(x=>x.Name)]\"> \r\n");
            output.Append("   <CellTemplate>\r\n");
            output.Append($"      <div class=\"d-flex flex-column\">\r\n");
            if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultFieldName.First()).Any())
            {
                output.Append($"        <MudText Typo=\"Typo.body2\">@context.Item.Name</MudText>\r\n");
            }
            if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultFieldName.Last()).Any())
            {
                output.Append($"        <MudText Typo=\"Typo.body2\" Class=\"mud-text-secondary\">@context.Item.Description</MudText>\r\n");
            }
            output.Append($"     </div>\r\n");
            output.Append("    </CellTemplate>\r\n");
            output.Append($"</PropertyColumn>\r\n");
        }
        foreach (var property in classObject.Properties.Where(x => !defaultFieldName.Contains(x.Name)))
        {
            if (property.Name == PRIMARYKEY) continue;
            output.Append("                ");
            output.Append($"<PropertyColumn Property=\"x => x.{property.Name}\" Title=\"@L[_currentDto.GetMemberDescription(x=>x.{property.Name})]\" />\r\n");
        }
        return output.ToString();
    }

    private static string CreateMudFormFieldDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
        {
            if (property.Name == PRIMARYKEY) continue;
            switch (property.Type.CodeName.ToLower())
            {
                case "string" when property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase):
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"true\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "string" when property.Name.Equals("Description", StringComparison.OrdinalIgnoreCase):
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" Lines=\"3\" For=\"@(() => model.{property.Name})\" @bind-Value=\"model.{property.Name}\"></MudTextField>\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "bool?":
                case "bool":
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudCheckBox Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Checked=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" ></MudCheckBox>\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "int?":
                case "int":
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "decimal?":
                case "decimal":
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00m\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "double?":
                case "double":
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                case "system.datetime?":
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudDatePicker Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Date=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudDatePicker>\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;
                default:
                    output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
                    output.Append("                ");
                    output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
                    output.Append("                ");
                    output.Append($"</MudItem> \r\n");
                    break;

            }

        }
        return output.ToString();
    }

    private static string CreateFieldAssignmentDefinition(IntellisenseObject classObject)
    {
        var output = new StringBuilder();
        foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
        {
            output.Append($"        ");
            output.Append($"        {property.Name} = dto.{property.Name}, \r\n");
        }
        return output.ToString();
    }
}
